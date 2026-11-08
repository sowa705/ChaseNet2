using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ChaseNet2.Serialization;
using ChaseNet2.Transport.Messages;
using Org.BouncyCastle.Crypto;
using Serilog;

namespace ChaseNet2.Transport
{
    /// <summary>
    /// Owns the UDP socket and all connections.
    /// <para>
    /// Two ways to run it, sharing one core:
    /// <list type="bullet">
    /// <item><b>Manual</b>: call <see cref="Update"/> regularly. Each call pumps the network (receive, acks, resends,
    /// pings, send) and then dispatches received messages and handler callbacks, all on the calling thread.</item>
    /// <item><b>Threaded</b>: call <see cref="Start"/> once. A dedicated network thread pumps the network on its own, so
    /// pings are answered and connections stay alive even when the application thread stalls. The application thread
    /// calls <see cref="Dispatch"/> (or <see cref="Update"/>, which then only dispatches) to run queued message handlers,
    /// <see cref="ConnectionHandler"/> callbacks and the <see cref="ConnectionAttached"/>/<see cref="ConnectionRemoved"/> events.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Callable from any thread: <see cref="CreateConnection"/>, <see cref="AttachConnectionAsync"/>, <see cref="RemoveConnection"/>,
    /// <see cref="AttachHandler"/>, <see cref="DetachHandler"/>, <see cref="Connections"/>, <see cref="Statistics"/>,
    /// <see cref="Serializer"/> and <see cref="Connection.EnqueueMessage"/>. Only one thread may dispatch at a time.
    /// </para>
    /// </summary>
    public class ConnectionManager : IDisposable
    {
        internal const ulong ConnectionRequestId = 0xADDDDDDDD;
        internal const ulong ConnectionResponseId = 0xBDDDDDDDD;
        private const int MaxPacketsPerReceiveBatch = 64;
        private const int MaxPacketsPerPump = 1024;

        private readonly AsymmetricCipherKeyPair _keyPair;
        public AsymmetricKeyParameter PublicKey { get => _keyPair.Public; }
        private readonly UdpClient _client;

        private readonly object _connectionsLock = new object();
        private volatile ImmutableList<Connection> _connections = ImmutableList<Connection>.Empty;
        private readonly object _handlersLock = new object();
        private volatile ImmutableList<ConnectionHandler> _handlers = ImmutableList<ConnectionHandler>.Empty;

        /// <summary>
        /// Snapshot of the current connections. Safe to enumerate from any thread; it does not change underneath you.
        /// </summary>
        public ImmutableList<Connection> Connections => _connections;

        /// <summary>
        /// Snapshot of the attached handlers.
        /// </summary>
        public ImmutableList<ConnectionHandler> Handlers => _handlers;

        public NetworkStatistics Statistics { get; private set; }
        public SerializationManager Serializer { get; private set; }

        public TransportSettings Settings { get; set; }

        /// <summary>
        /// Raised on the dispatching thread when a connection is attached: an accepted incoming connection or
        /// <see cref="AttachConnectionAsync"/>. Not raised for <see cref="CreateConnection"/>.
        /// </summary>
        public event Action<Connection>? ConnectionAttached;

        /// <summary>
        /// Raised on the dispatching thread after <see cref="RemoveConnection"/>.
        /// </summary>
        public event Action<Connection>? ConnectionRemoved;

        // --- network thread state ---
        private readonly ConcurrentQueue<Connection> _closingConnections = new ConcurrentQueue<Connection>();
        private readonly Stopwatch _statisticsStopwatch = Stopwatch.StartNew();
        private long _lastSentBytes;
        private long _lastReceivedBytes;

        // --- threaded mode ---
        private readonly object _lifecycleLock = new object();
        private Thread? _networkThread;
        private volatile bool _stopRequested;
        private volatile bool _disposed;
        private CancellationTokenSource? _legacyDispatchLoop;

        // --- dispatch queue ---
        private readonly object _dispatchLock = new object();
        private List<DispatchItem> _dispatchQueue = new List<DispatchItem>();
        private readonly Dictionary<(Connection, ulong), DispatchItem> _pendingSequenced = new Dictionary<(Connection, ulong), DispatchItem>();
        private readonly object _dispatchGate = new object();
        private bool _queueFullWarned;

        public ConnectionManager(int? port = null) : this(new TransportSettings(), port)
        {
        }

        public ConnectionManager(TransportSettings settings, int? port = null)
        {
            Settings = settings;

            _keyPair = CryptoHelper.GenerateKeyPair();

            _client = port == null ? new UdpClient() : new UdpClient(port.Value);
            _client.Client.ReceiveBufferSize = Settings.ReceiveBufferSize;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Without this, an ICMP "port unreachable" from a peer that went away makes the next Receive throw
                // and keeps the socket readable, which would spin the network thread.
                const int SIO_UDP_CONNRESET = -1744830452;
                try
                {
                    _client.Client.IOControl(SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
                }
                catch (Exception e)
                {
                    Log.Logger.Debug("Could not disable UDP connection reset reporting ({e})", e.Message);
                }
            }

            Statistics = new NetworkStatistics();
            Serializer = new SerializationManager();
            Serializer.RegisterChaseNetTypes();
        }

        /// <summary>
        /// The local UDP endpoint the manager listens on.
        /// </summary>
        public IPEndPoint? LocalEndPoint => _client.Client.LocalEndPoint as IPEndPoint;

        /// <summary>
        /// True while the dedicated network thread runs (see <see cref="Start"/>).
        /// </summary>
        public bool IsRunning
        {
            get
            {
                lock (_lifecycleLock)
                {
                    return _networkThread != null;
                }
            }
        }

        /// <summary>
        /// Add a connection to a peer whose public key is known (for example from a tracker). Callable from any thread.
        /// Handlers' <see cref="ConnectionHandler.OnManagerConnect"/> and <see cref="ConnectionAttached"/> run on the
        /// next dispatch; the returned task completes immediately.
        /// </summary>
        public Task<Connection> AttachConnectionAsync(ConnectionTarget target)
        {
            ThrowIfDisposed();
            var c = new Connection(this, target);
            AddConnection(c);
            EnqueueDispatchEvent(DispatchKind.ConnectionAttached, c);
            return Task.FromResult(c);
        }

        /// <summary>
        /// Remove a connection. Callable from any thread. It disappears from <see cref="Connections"/> immediately,
        /// its undelivered reliable messages fail, and handlers are notified on the next dispatch.
        /// </summary>
        public void RemoveConnection(ulong connectionId)
        {
            Connection? removed;
            lock (_connectionsLock)
            {
                removed = _connections.Find(c => c.ConnectionId == connectionId);
                if (removed == null)
                {
                    return;
                }
                _connections = _connections.Remove(removed);
                Statistics.ConnectionCount = _connections.Count;
            }

            removed.MarkClosed();
            // the rest of its state belongs to the network thread
            _closingConnections.Enqueue(removed);
            EnqueueDispatchEvent(DispatchKind.ConnectionRemoved, removed);
        }

        /// <summary>
        /// Open a connection to a listening peer. Callable from any thread; the request is sent by the network thread.
        /// </summary>
        public Connection CreateConnection(IPEndPoint endPoint)
        {
            ThrowIfDisposed();
            var bytes = new byte[8];
            RandomNumberGenerator.Fill(bytes);
            var id = BitConverter.ToUInt64(bytes, 0);

            var c = new Connection(this, new ConnectionTarget() { EndPoint = endPoint, PublicKey = null, ConnectionId = id });
            c.RequestConnect();
            AddConnection(c);

            Log.Logger.Information("Created connection to {0} with id {1}", endPoint, id);

            return c;
        }

        private void AddConnection(Connection c)
        {
            lock (_connectionsLock)
            {
                _connections = _connections.Add(c);
                Statistics.ConnectionCount = _connections.Count;
            }
        }

        public void AttachHandler(ConnectionHandler connectionHandler)
        {
            lock (_handlersLock)
            {
                _handlers = _handlers.Add(connectionHandler);
            }
            connectionHandler.OnAttached(this).Wait();
        }

        public void DetachHandler(ConnectionHandler connectionHandler)
        {
            lock (_handlersLock)
            {
                _handlers = _handlers.Remove(connectionHandler);
            }
        }

        #region Threaded mode

        /// <summary>
        /// Start the dedicated network thread. From now on the network is pumped in the background and the
        /// application calls <see cref="Dispatch"/> (or <see cref="Update"/>) to receive messages and callbacks.
        /// </summary>
        public void Start()
        {
            lock (_lifecycleLock)
            {
                ThrowIfDisposed();
                if (_networkThread != null)
                {
                    throw new InvalidOperationException("The network thread is already running");
                }

                _stopRequested = false;
                _networkThread = new Thread(NetworkThreadLoop)
                {
                    IsBackground = true,
                    Name = "ChaseNet2 network",
                };
                _networkThread.Start();
            }
        }

        /// <summary>
        /// Stop the network thread and wait for it to exit. The manager can then be pumped manually again.
        /// </summary>
        public void Stop()
        {
            Thread? thread;
            lock (_lifecycleLock)
            {
                thread = _networkThread;
                if (thread == null)
                {
                    return;
                }
                _stopRequested = true;
            }

            if (thread != Thread.CurrentThread)
            {
                thread.Join();
            }

            lock (_lifecycleLock)
            {
                if (_networkThread == thread)
                {
                    _networkThread = null;
                }
            }
        }

        /// <summary>
        /// Runs the network on a dedicated thread and handler dispatch on a thread-pool loop at
        /// <see cref="TransportSettings.TargetUpdateRate"/>.
        /// </summary>
        [Obsolete("Use Start() and call Dispatch() (or Update()) from the thread that should run message handlers.")]
        public void StartBackgroundThread()
        {
            Start();

            var cts = new CancellationTokenSource();
            _legacyDispatchLoop = cts;
            Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested && !_disposed)
                {
                    Dispatch();
                    await Task.Delay(TimeSpan.FromSeconds(1f / Math.Max(1f, Settings.TargetUpdateRate)), cts.Token);
                }
            }, cts.Token).ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    Log.Logger.Error("Background dispatch loop crashed: {0}", t.Exception);
                }
            });
        }

        private void NetworkThreadLoop()
        {
            var clock = Stopwatch.StartNew();
            var nextTick = TimeSpan.Zero;

            try
            {
                while (!_stopRequested)
                {
                    if (clock.Elapsed >= nextTick)
                    {
                        try
                        {
                            Pump();
                        }
                        catch (ObjectDisposedException)
                        {
                            break;
                        }
                        catch (Exception e)
                        {
                            Log.Logger.Error(e, "Network update failed");
                        }

                        var interval = TimeSpan.FromSeconds(1.0 / Math.Max(1f, Settings.TargetUpdateRate));
                        nextTick += interval;
                        if (nextTick < clock.Elapsed)
                        {
                            // fell behind (slow tick or a debugger pause); do not try to catch up with a burst
                            nextTick = clock.Elapsed + interval;
                        }
                    }

                    // Wait for the next tick, but read packets as soon as they arrive so pings are answered and the
                    // socket buffer is drained promptly. The cap keeps Stop() responsive.
                    var remaining = nextTick - clock.Elapsed;
                    if (remaining > TimeSpan.Zero)
                    {
                        var micro = (int)Math.Min(remaining.TotalMicroseconds, 50_000);
                        if (micro > 0 && _client.Client.Poll(micro, SelectMode.SelectRead))
                        {
                            // bounded, so a steady inflow cannot starve the tick that sends acks; the loop comes
                            // straight back here (Poll returns at once) while the tick is not due
                            ReceiveAvailable(readAtLeastOne: true, MaxPacketsPerReceiveBatch);
                        }
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // socket closed under us during shutdown
            }
            catch (Exception e)
            {
                Log.Logger.Fatal(e, "Network thread crashed");
            }
            finally
            {
                lock (_lifecycleLock)
                {
                    if (_networkThread == Thread.CurrentThread)
                    {
                        _networkThread = null;
                    }
                }
            }
        }

        #endregion

        #region Pump and dispatch

        /// <summary>
        /// Manual mode: pump the network and dispatch on the calling thread.
        /// Threaded mode (after <see cref="Start"/>): only dispatch; the network thread does the pumping.
        /// </summary>
        /// <returns>The time the network pump took (zero in threaded mode).</returns>
        public Task<TimeSpan> Update()
        {
            ThrowIfDisposed();
            var elapsed = TimeSpan.Zero;
            if (!IsRunning)
            {
                elapsed = Pump();
            }
            Dispatch();
            return Task.FromResult(elapsed);
        }

        /// <summary>
        /// Run everything queued for the application thread: <see cref="ConnectionHandler.OnManagerConnect"/>,
        /// <see cref="ConnectionAttached"/>, queued message handlers (messages without a handler go to
        /// <see cref="Connection.IncomingMessages"/>), <see cref="ConnectionHandler.OnManagerDisconnect"/>,
        /// <see cref="ConnectionRemoved"/>, then <see cref="ConnectionHandler.ConnectionUpdate"/> and
        /// <see cref="ConnectionHandler.Update"/>. Items run in arrival order.
        /// </summary>
        /// <returns>Number of queued events and messages dispatched.</returns>
        public int Dispatch()
        {
            if (!Monitor.TryEnter(_dispatchGate))
            {
                throw new InvalidOperationException("Dispatch is already running on another thread; only one thread may dispatch.");
            }

            try
            {
                List<DispatchItem> items;
                lock (_dispatchLock)
                {
                    items = _dispatchQueue;
                    if (items.Count > 0)
                    {
                        _dispatchQueue = new List<DispatchItem>();
                        _pendingSequenced.Clear();
                        _queueFullWarned = false;
                    }
                }

                if (items.Count > 0)
                {
                    var handlers = _handlers;
                    foreach (var item in items)
                    {
                        DispatchOne(item, handlers);
                    }
                }

                var connections = _connections;
                foreach (var handler in _handlers)
                {
                    foreach (var connection in connections)
                    {
                        if (handler.ShouldHandle(connection.ConnectionId))
                        {
                            handler.ConnectionUpdate(connection);
                        }
                    }
                }

                foreach (var handler in _handlers)
                {
                    handler.Update();
                }

                return items.Count;
            }
            finally
            {
                Monitor.Exit(_dispatchGate);
            }
        }

        private void DispatchOne(DispatchItem item, ImmutableList<ConnectionHandler> handlers)
        {
            var connection = item.Connection;
            switch (item.Kind)
            {
                case DispatchKind.Message:
                    connection.DeliverQueuedMessage(item.Message!);
                    break;

                case DispatchKind.ConnectionAttached:
                    foreach (var handler in handlers)
                    {
                        try
                        {
                            var task = handler.OnManagerConnect(connection);
                            if (!task.IsCompleted)
                            {
                                task.ContinueWith(t => Log.Logger.Error(t.Exception, "OnManagerConnect failed"), TaskContinuationOptions.OnlyOnFaulted);
                            }
                            else if (task.IsFaulted)
                            {
                                Log.Logger.Error(task.Exception, "OnManagerConnect failed");
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Logger.Error(e, "OnManagerConnect failed");
                        }
                    }
                    InvokeEvent(ConnectionAttached, connection);
                    break;

                case DispatchKind.ConnectionRemoved:
                    foreach (var handler in handlers)
                    {
                        try
                        {
                            handler.OnManagerDisconnect(connection);
                        }
                        catch (Exception e)
                        {
                            Log.Logger.Error(e, "OnManagerDisconnect failed");
                        }
                    }
                    InvokeEvent(ConnectionRemoved, connection);
                    break;
            }
        }

        private static void InvokeEvent(Action<Connection>? handler, Connection connection)
        {
            if (handler == null)
            {
                return;
            }
            try
            {
                handler(connection);
            }
            catch (Exception e)
            {
                Log.Logger.Error(e, "Connection event handler failed");
            }
        }

        /// <summary>
        /// Network thread: queue a received message for the dispatching thread.
        /// </summary>
        internal void EnqueueForDispatch(Connection connection, NetworkMessage message)
        {
            lock (_dispatchLock)
            {
                bool sequenced = message.Type.HasFlag(MessageType.Sequenced);
                if (sequenced && _pendingSequenced.TryGetValue((connection, message.ChannelID), out var pending))
                {
                    // only the newest state matters; replace the queued one in place
                    pending.Message = message;
                    return;
                }

                if (!message.Type.HasFlag(MessageType.Reliable) && _dispatchQueue.Count >= Settings.MaxPendingDispatchItems)
                {
                    Statistics.AddDroppedReceivedMessage();
                    if (!_queueFullWarned)
                    {
                        _queueFullWarned = true;
                        Log.Logger.Warning("Dispatch queue is full ({Count} items); dropping unreliable messages until Dispatch() runs", _dispatchQueue.Count);
                    }
                    return;
                }

                var item = new DispatchItem(DispatchKind.Message, connection, message);
                _dispatchQueue.Add(item);
                if (sequenced)
                {
                    _pendingSequenced[(connection, message.ChannelID)] = item;
                }
            }
        }

        private void EnqueueDispatchEvent(DispatchKind kind, Connection connection)
        {
            lock (_dispatchLock)
            {
                _dispatchQueue.Add(new DispatchItem(kind, connection, null));
            }
        }

        /// <summary>
        /// One network step on the calling thread: read all available packets, then update every connection
        /// (resends, pings, timeouts, sending).
        /// </summary>
        private TimeSpan Pump()
        {
            var stopwatch = Stopwatch.StartNew();

            while (_closingConnections.TryDequeue(out var closing))
            {
                closing.FailOutstandingMessages();
            }

            // bounded so connection updates (acks, resends, sends) always run, even under a flood
            ReceiveAvailable(readAtLeastOne: false, MaxPacketsPerPump);

            var connections = _connections;
            foreach (var connection in connections)
            {
                try
                {
                    connection.Update();
                }
                catch (Exception e) when (e is not ObjectDisposedException)
                {
                    Log.Logger.Error(e, "Failed to update connection {ConnectionId}", connection.ConnectionId);
                }
            }

            stopwatch.Stop();

            var average = Statistics.AverageUpdateTime;
            Statistics.AverageUpdateTime = (average + stopwatch.Elapsed) / 2;
            if (connections.Count > 0)
            {
                Statistics.AveragePing = connections.Average(x => x.AveragePing);
            }

            var statisticsElapsed = _statisticsStopwatch.Elapsed.TotalSeconds;
            if (statisticsElapsed >= 1)
            {
                _statisticsStopwatch.Restart();
                var sent = Statistics.BytesSent;
                var received = Statistics.BytesReceived;
                Statistics.BitsSentPerSecond = (float)((sent - _lastSentBytes) * 8 / statisticsElapsed);
                Statistics.BitsReceivedPerSecond = (float)((received - _lastReceivedBytes) * 8 / statisticsElapsed);
                _lastSentBytes = sent;
                _lastReceivedBytes = received;
            }

            return stopwatch.Elapsed;
        }

        private void ReceiveAvailable(bool readAtLeastOne, int maxPackets)
        {
            // readAtLeastOne: Poll reported the socket readable, which can also mean an empty datagram or a pending
            // error with Available == 0; one Receive consumes it so the next Poll does not return immediately again.
            for (int received = 0; received < maxPackets && (readAtLeastOne || _client.Available > 0); received++)
            {
                readAtLeastOne = false;

                var remoteEP = new IPEndPoint(IPAddress.Any, 0);
                byte[] data;
                try
                {
                    data = _client.Receive(ref remoteEP);
                }
                catch (SocketException e)
                {
                    Log.Logger.Debug("Error receiving packet: {0}", e.Message);
                    continue;
                }

                ProcessIncomingPacket(data, remoteEP);
            }
        }

        private void ProcessIncomingPacket(byte[] data, IPEndPoint remoteEP)
        {
            if (data.Length < 8)
            {
                return;
            }

            var targetConnection = BitConverter.ToUInt64(data, 0);

            Statistics.AddReceivedPacket(data.Length);

            using var ms = new MemoryStream(data, 8, data.Length - 8); // skip first 8 bytes

            if (targetConnection == ConnectionRequestId)
            {
                HandleConnectionRequest(ms, remoteEP);
                return;
            }
            if (targetConnection == ConnectionResponseId)
            {
                HandleConnectionResponse(ms, remoteEP);
                return;
            }

            var c = _connections.Find(x => x.ConnectionId == targetConnection);
            if (c is null)
            {
                return;
            }
            try
            {
                c.ReadInputStream(ms);
            }
            catch (Exception e)
            {
                Log.Logger.Error(e, "Error processing packet from {EndPoint} for connection {ConnectionId}", remoteEP, targetConnection);
            }
        }

        private void HandleConnectionRequest(MemoryStream ms, IPEndPoint remoteEP)
        {
            Log.Logger.Information("Received connection request from {EndPoint}", remoteEP);

            if (!Settings.AcceptNewConnections)
            {
                Log.Logger.Warning("Received connection request from {EndPoint} but new connections are not accepted", remoteEP);
                return;
            }

            try
            {
                var request = Serializer.Deserialize<ConnectionRequest>(new BinaryReader(ms));
                if (request == null)
                {
                    return;
                }

                var existing = _connections.Find(x => x.ConnectionId == request.ConnectionId);
                if (existing != null)
                {
                    if (existing.RemoteEndpoint.Equals(remoteEP))
                    {
                        // our response was lost and the peer retried
                        existing.SendConnectionResponse();
                    }
                    else
                    {
                        Log.Logger.Warning("Connection with id {0} already exists, rejecting connection request", request.ConnectionId);
                    }
                    return;
                }

                if (_connections.Count >= Settings.MaximumConnections)
                {
                    Log.Logger.Warning("Rejecting connection request from {EndPoint}: connection limit reached", remoteEP);
                    return;
                }

                var connection = new Connection(this, new ConnectionTarget()
                {
                    EndPoint = remoteEP,
                    PublicKey = request.PublicKey,
                    ConnectionId = request.ConnectionId
                });
                AddConnection(connection);
                // handlers learn about it before any of its messages are dispatched
                EnqueueDispatchEvent(DispatchKind.ConnectionAttached, connection);

                connection.SendConnectionResponse();

                Log.Logger.Information("Attached a new connection from {EndPoint} with id {ConnectionId}", remoteEP, request.ConnectionId);
            }
            catch (Exception e)
            {
                Log.Logger.Error("Error processing connection request: {0}", e);
            }
        }

        private void HandleConnectionResponse(MemoryStream ms, IPEndPoint remoteEP)
        {
            Log.Logger.Information("Received connection response from {EndPoint}", remoteEP);
            try
            {
                var response = Serializer.Deserialize<ConnectionResponse>(new BinaryReader(ms));
                var connection = response == null ? null : _connections.Find(x => x.ConnectionId == response.ConnectionId);
                if (connection == null)
                {
                    Log.Logger.Warning("Received a connection response from {EndPoint} for an unknown connection", remoteEP);
                    return;
                }
                if (!response!.Accepted)
                {
                    Log.Logger.Warning("Connection request was rejected by {EndPoint} :(", remoteEP);
                    connection.SetState(ConnectionState.Disabled);
                    return;
                }
                if (connection.PeerPublicKey == null)
                {
                    connection.SetPeerPublicKey(response.PublicKey);
                }
                connection.SetState(ConnectionState.Connected);
                Log.Logger.Information("Connection {ConnectionID} established with {EndPoint}", response.ConnectionId, remoteEP);
            }
            catch (Exception e)
            {
                Log.Logger.Error("Error processing connection response: {0}", e);
            }
        }

        #endregion

        #region Crypto and sending

        public byte[] ComputeSharedSecretKey(AsymmetricKeyParameter remotePublicKey, ulong connectionID)
        {
            byte[] sharedSecret = CryptoHelper.GenerateDHKey(_keyPair.Private, remotePublicKey);

            // add connection id to shared secret
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            sha.AppendData(sharedSecret);
            sha.AppendData(BitConverter.GetBytes(connectionID));
            return sha.GetHashAndReset();
        }

        public Aes CreateAes(byte[] key, byte[] iv)
        {
            var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            return aes;
        }

        public byte[] GenerateInitializationVector()
        {
            var iv = new byte[16];
            RandomNumberGenerator.Fill(iv);
            return iv;
        }

        /// <summary>
        /// Network thread: send one datagram.
        /// </summary>
        internal void SendPacket(byte[] array, IPEndPoint remoteEndpoint, ulong connectionId)
        {
            var data = new byte[array.Length + 8];
            BitConverter.GetBytes(connectionId).CopyTo(data, 0);
            array.CopyTo(data, 8);

            if (Settings.SimulatedPacketLoss <= 0 || Random.Shared.NextDouble() >= Settings.SimulatedPacketLoss)
            {
                try
                {
                    _client.Send(data, data.Length, remoteEndpoint);
                }
                catch (SocketException e)
                {
                    Log.Logger.Warning("Failed to send packet to {EndPoint}: {Error}", remoteEndpoint, e.Message);
                }
            }
            else
            {
                Log.Logger.Debug("Dropping packet to {EndPoint}", remoteEndpoint);
            }

            Statistics.AddSentPacket(data.Length);
        }

        #endregion

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ConnectionManager));
            }
        }

        /// <summary>
        /// Stop the network thread, close the socket and fail every undelivered reliable message.
        /// </summary>
        public void Dispose()
        {
            lock (_lifecycleLock)
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
            }

            _legacyDispatchLoop?.Cancel();
            Stop();
            _client.Dispose();

            // the network thread is gone (unless Dispose was called from it), so its state can be touched here
            while (_closingConnections.TryDequeue(out var closing))
            {
                closing.FailOutstandingMessages();
            }
            foreach (var connection in _connections)
            {
                connection.FailOutstandingMessages();
            }

            GC.SuppressFinalize(this);
        }

        private enum DispatchKind
        {
            Message,
            ConnectionAttached,
            ConnectionRemoved,
        }

        private sealed class DispatchItem
        {
            public DispatchKind Kind { get; }
            public Connection Connection { get; }
            public NetworkMessage? Message { get; set; }

            public DispatchItem(DispatchKind kind, Connection connection, NetworkMessage? message)
            {
                Kind = kind;
                Connection = connection;
                Message = message;
            }
        }
    }
}
