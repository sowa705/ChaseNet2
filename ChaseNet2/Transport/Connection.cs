using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ChaseNet2.Extensions;
using ChaseNet2.Transport.Messages;
using Org.BouncyCastle.Crypto;
using Serilog;

namespace ChaseNet2.Transport
{
    /// <summary>
    /// A point to point link to a peer.
    /// <para>
    /// Threading: the network thread (the thread pumping the <see cref="ConnectionManager"/>) owns all transport state
    /// (send queues, acks, resends, pings, split reassembly). <see cref="EnqueueMessage"/>, handler registration,
    /// <see cref="WaitForDeliveryAsync"/>, <see cref="WaitForChannelMessageAsync"/>, <see cref="SetState"/> and the
    /// public read-only properties may be used from any thread.
    /// </para>
    /// </summary>
    public partial class Connection
    {
        public AsymmetricKeyParameter? PeerPublicKey;
        public ulong ConnectionId { get; private set; }

        public IPEndPoint RemoteEndpoint;
        private readonly ConnectionManager _manager;

        private int _state = (int)ConnectionState.Started;

        public ConnectionState State => (ConnectionState)Volatile.Read(ref _state);

        private volatile byte[]? _sharedKey;

        /// <summary>
        /// Messages that arrived on a channel with no registered handler. Filled on the dispatching thread.
        /// </summary>
        public ConcurrentQueue<NetworkMessage> IncomingMessages { get; private set; }

        // --- owned by the network thread ---
        private readonly LinkedList<NetworkMessage> _outgoingPriorityMessages = new LinkedList<NetworkMessage>();
        private readonly LinkedList<NetworkMessage> _outgoingNonPriorityMessages = new LinkedList<NetworkMessage>();
        private readonly Dictionary<ulong, SentMessage> _trackedSentMessages = new Dictionary<ulong, SentMessage>();
        // Messages that are waiting for other parts to arrive
        private readonly Dictionary<ulong, SplitReceivedMessage> _splitReceivedMessages = new Dictionary<ulong, SplitReceivedMessage>();
        private readonly HashSet<ulong> _completedSplitMessageIds = new HashSet<ulong>();
        private readonly Dictionary<ulong, ulong> _sequencedMessageIds = new Dictionary<ulong, ulong>();
        private readonly HashSet<ulong> _receivedMessageIds = new HashSet<ulong>();
        private readonly Queue<ulong> _receivedMessageIdOrder = new Queue<ulong>();
        private readonly Random _rng = new Random();
        private bool _isOutgoing;
        private bool _connectRequestPending;
        private int _connectionRetryCount;

        // --- outgoing messages handed over from any thread ---
        private readonly object _pendingOutgoingLock = new object();
        private List<NetworkMessage> _pendingOutgoing = new List<NetworkMessage>();
        private bool _closed;

        private readonly ConcurrentDictionary<ulong, HandlerRegistration> _messageHandlers = new ConcurrentDictionary<ulong, HandlerRegistration>();

        private long _lastMessageId;
        private long _lastPingTicks;
        private long _lastReceivedPongTicks;
        private float _averagePing;

        /// <summary>
        /// The ID the next enqueued message will get.
        /// </summary>
        public ulong CurrentMessageId => (ulong)Interlocked.Read(ref _lastMessageId) + 1;

        public DateTime LastPing
        {
            get => new DateTime(Interlocked.Read(ref _lastPingTicks), DateTimeKind.Utc);
            private set => Interlocked.Exchange(ref _lastPingTicks, value.Ticks);
        }

        public DateTime LastReceivedPong
        {
            get => new DateTime(Interlocked.Read(ref _lastReceivedPongTicks), DateTimeKind.Utc);
            private set => Interlocked.Exchange(ref _lastReceivedPongTicks, value.Ticks);
        }

        public int RandomPingNumber { get; private set; }

        public float AveragePing
        {
            get => Volatile.Read(ref _averagePing);
            private set => Volatile.Write(ref _averagePing, value);
        }

        public DateTime LastConnectionAttempt { get; private set; }

        /// <summary>
        /// Snapshot of the registered message handlers by channel.
        /// </summary>
        public IReadOnlyDictionary<ulong, IMessageHandler> MessageHandlers =>
            _messageHandlers.ToDictionary(x => x.Key, x => x.Value.Handler);

        /// <summary>
        /// True once the connection was removed from its manager; enqueued messages then fail immediately.
        /// </summary>
        public bool IsClosed
        {
            get
            {
                lock (_pendingOutgoingLock)
                {
                    return _closed;
                }
            }
        }

        public Connection(ConnectionManager manager, ConnectionTarget target)
        {
            PeerPublicKey = target.PublicKey;
            RemoteEndpoint = target.EndPoint;
            ConnectionId = target.ConnectionId;

            _manager = manager;

            if (PeerPublicKey != null)
            {
                SetPeerPublicKey(PeerPublicKey);
            }

            IncomingMessages = new ConcurrentQueue<NetworkMessage>();

            LastPing = DateTime.UtcNow;
            LastReceivedPong = DateTime.UtcNow;
            LastConnectionAttempt = DateTime.UtcNow;

            RegisterMessageHandler((ulong)InternalChannelType.ConnectionInternal, new InternalMessageHandler(), MessageDispatchMode.Inline);
        }

        /// <summary>
        /// Queue a message for sending. Callable from any thread.
        /// The content is serialized immediately, so the caller may keep mutating the object afterwards.
        /// </summary>
        public NetworkMessage EnqueueMessage(MessageType type, ulong channelID, object obj)
        {
            if (obj is null)
            {
                throw new ArgumentNullException(nameof(obj), "Message content cannot be null");
            }

            if (type.HasFlag(MessageType.Reliable) && type.HasFlag(MessageType.Sequenced))
            {
                throw new NotImplementedException("Cannot create a message with reliable and sequenced type"); //TODO: implement this in the future
            }

            var message = CreateOutgoingMessage(type, channelID, obj);

            if (type.HasFlag(MessageType.Reliable))
            {
                message.DeliveryCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            if (message.SerializedContent!.Length > _manager.Settings.MaxMessageLength)
            {
                Log.Logger.Error("Message size {Size} above the max setting {MaxSize}", message.SerializedContent.Length, _manager.Settings.MaxMessageLength);
                message.MarkFailed();
                return message;
            }

            lock (_pendingOutgoingLock)
            {
                if (!_closed)
                {
                    _pendingOutgoing.Add(message);
                    return message;
                }
            }

            message.MarkFailed();
            return message;
        }

        private NetworkMessage CreateOutgoingMessage(MessageType type, ulong channelID, object obj)
        {
            var bytes = _manager.Serializer.Serialize(obj);
            var id = (ulong)Interlocked.Increment(ref _lastMessageId);
            return new NetworkMessage(id, channelID, type, obj) { SerializedContent = bytes, State = MessageState.Created };
        }

        /// <summary>
        /// Network thread only: queue a transport message directly into the send lists.
        /// </summary>
        private NetworkMessage EnqueueInternalMessage(MessageType type, object obj, SentMessage? splitParent = null)
        {
            var message = CreateOutgoingMessage(type, (ulong)InternalChannelType.ConnectionInternal, obj);
            AddToOutgoing(message, splitParent);
            return message;
        }

        private void AddToOutgoing(NetworkMessage message, SentMessage? splitParent = null)
        {
            if (message.Type.HasFlag(MessageType.Priority))
            {
                _outgoingPriorityMessages.AddLast(message);
            }
            else
            {
                _outgoingNonPriorityMessages.AddLast(message);
            }

            if (message.Type.HasFlag(MessageType.Reliable)) //we need to track this message for later
            {
                _trackedSentMessages[message.ID] = new SentMessage(message) { SplitParent = splitParent };
            }
        }

        /// <summary>
        /// Register a handler for a channel. Callable from any thread.
        /// </summary>
        /// <param name="dispatchMode">
        /// <see cref="MessageDispatchMode.Queued"/> (default) runs the handler on the dispatching thread;
        /// <see cref="MessageDispatchMode.Inline"/> runs it on the network thread.
        /// </param>
        public void RegisterMessageHandler(ulong channelID, IMessageHandler handler, MessageDispatchMode dispatchMode = MessageDispatchMode.Queued)
        {
            if (!_messageHandlers.TryAdd(channelID, new HandlerRegistration(handler, dispatchMode)))
            {
                throw new ArgumentException($"A message handler is already registered for channel {channelID}");
            }
        }

        public void UnregisterMessageHandler(ulong channelID)
        {
            _messageHandlers.TryRemove(channelID, out _);
        }

        /// <summary>
        /// Completes with true when the peer acknowledges a reliable message, false when delivery fails.
        /// </summary>
        public Task<bool> WaitForDeliveryAsync(NetworkMessage message)
        {
            if (message.DeliveryCompletion is { } completion)
            {
                return completion.Task;
            }

            if (!message.Type.HasFlag(MessageType.Reliable))
            {
                throw new InvalidOperationException("Delivery can only be awaited for reliable messages");
            }

            return Task.FromResult(message.State == MessageState.Delivered);
        }

        /// <summary>
        /// Asynchronously wait for a message to arrive on a specific channel. A message that arrived earlier and
        /// has not been dispatched yet is also delivered to the waiter.
        /// </summary>
        public async Task<NetworkMessage> WaitForChannelMessageAsync(ulong channelID, TimeSpan timeout)
        {
            Log.Information("Waiting for channel message on channel {channelID}", channelID);

            var handler = new TaskHandler();
            var registration = new HandlerRegistration(handler, MessageDispatchMode.Inline);
            if (!_messageHandlers.TryAdd(channelID, registration))
            {
                throw new ArgumentException($"A message handler is already registered for channel {channelID}");
            }

            try
            {
                var task = handler.TaskCompletionSource.Task;
                var completedTask = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
                if (completedTask != task)
                {
                    throw new TimeoutException("Timed out waiting for channel message");
                }
                return await task.ConfigureAwait(false);
            }
            finally
            {
                _messageHandlers.TryRemove(new KeyValuePair<ulong, HandlerRegistration>(channelID, registration));
            }
        }

        /// <summary>
        /// Send the connection request on the next network update (outgoing connections only).
        /// </summary>
        internal void RequestConnect()
        {
            _isOutgoing = true;
            _connectRequestPending = true;
        }

        internal void SendConnectRequest()
        {
            ConnectionRequest request = new ConnectionRequest() { PublicKey = _manager.PublicKey, ConnectionId = ConnectionId };
            _manager.SendPacket(_manager.Serializer.Serialize(request), RemoteEndpoint, ConnectionManager.ConnectionRequestId);
        }

        internal void SendConnectionResponse()
        {
            ConnectionResponse response = new ConnectionResponse() { PublicKey = _manager.PublicKey, Accepted = true, ConnectionId = ConnectionId };
            _manager.SendPacket(_manager.Serializer.Serialize(response), RemoteEndpoint, ConnectionManager.ConnectionResponseId);
        }

        public void SetPeerPublicKey(AsymmetricKeyParameter key)
        {
            _sharedKey = _manager.ComputeSharedSecretKey(key, ConnectionId);
            PeerPublicKey = key;
        }

        /// <summary>
        /// Set the connection state. Callable from any thread.
        /// </summary>
        public void SetState(ConnectionState state)
        {
            Volatile.Write(ref _state, (int)state);
        }

        private bool TryTransitionState(ConnectionState from, ConnectionState to)
        {
            return Interlocked.CompareExchange(ref _state, (int)to, (int)from) == (int)from;
        }

        /// <summary>
        /// A pong proves the link is alive; revive the connection unless the application disabled it.
        /// </summary>
        private void MarkAlive()
        {
            while (true)
            {
                var current = Volatile.Read(ref _state);
                if (current == (int)ConnectionState.Disabled || current == (int)ConnectionState.Connected)
                {
                    return;
                }
                if (Interlocked.CompareExchange(ref _state, (int)ConnectionState.Connected, current) == current)
                {
                    return;
                }
            }
        }

        internal void ReadInputStream(Stream stream)
        {
            var key = _sharedKey;
            if (PeerPublicKey == null || key == null)
            {
                return;
            }
            var initializationVector = new byte[16];
            var ivCount = stream.Read(initializationVector, 0, 16); //Read the initialization vector for the packet AES encryption

            if (ivCount != 16)
            {
                throw new Exception("Invalid initialization vector length");
            }

            // decrypt the packet
            using var aes = _manager.CreateAes(key, initializationVector);
            using var cs = new CryptoStream(stream, aes.CreateDecryptor(key, initializationVector), CryptoStreamMode.Read);

            // read packet preamble
            var reader = new BinaryReader(cs);

            try
            {
                var preamble = reader.ReadInt32();
                if (preamble != 0x12345678)
                {
                    Log.Logger.Warning("Received an invalid packet preamble");
                    return;
                }
            }
            catch (Exception e)
            {
                Log.Logger.Warning("Received an invalid packet ({e})", e);
                return;
            }

            try
            {
                // read messages (packet is a list of messages)
                while (true)
                {
                    var messageID = reader.ReadUInt64();
                    var channelID = reader.ReadUInt64();
                    var messageType = (MessageType)reader.ReadByte();
                    object messageContent;
                    try
                    {
                        messageContent = _manager.Serializer.Deserialize(reader);
                    }
                    catch (Exception e) when (e is not EndOfStreamException)
                    {
                        Log.Logger.Warning("Failed to deserialize message {messageID} ({e})", messageID, e);
                        break;
                    }

                    var message = new NetworkMessage(messageID, channelID, messageType, messageContent);
                    try
                    {
                        RouteReceivedMessage(message);
                    }
                    catch (Exception e)
                    {
                        // A faulty handler or malformed message must not take down the whole process.
                        Log.Logger.Error(e, "Failed to handle message {MessageID} on channel {ChannelID}", messageID, channelID);
                    }
                }
            }
            catch (EndOfStreamException)
            {
            }
            catch (CryptographicException e)
            {
                Log.Logger.Warning("Failed to decrypt packet ({e})", e.Message);
            }
        }

        private void RouteReceivedMessage(NetworkMessage message)
        {
            message.State = MessageState.Received;

            if (message.Type.HasFlag(MessageType.Sequenced))
            {
                if (message.Type.HasFlag(MessageType.Reliable))
                {
                    Log.Logger.Error("Received unsupported Reliable+Sequenced message {messageId}", message.ID);
                    return;
                }

                if (_sequencedMessageIds.TryGetValue(message.ChannelID, out var currentSeqId) && message.ID <= currentSeqId)
                {
                    // we already have a newer message/this is a duplicate
                    Log.Logger.Debug("Received an outdated sequenced message {MessageId} (newest {currentSeqId})", message.ID, currentSeqId);
                    return;
                }
                _sequencedMessageIds[message.ChannelID] = message.ID;
            }

            if (message.Type.HasFlag(MessageType.Reliable))
            {
                // acknowledge every copy: a duplicate means our previous ack was lost
                EnqueueInternalMessage(MessageType.Unreliable, new Ack() { MessageID = message.ID });
            }

            if (!_receivedMessageIds.Add(message.ID))
            {
                Log.Logger.Debug("Received a duplicate message {messageID}", message.ID);
                return;
            }

            _receivedMessageIdOrder.Enqueue(message.ID);
            while (_receivedMessageIdOrder.Count > 10000)
            {
                _receivedMessageIds.Remove(_receivedMessageIdOrder.Dequeue());
            }

            Log.Logger.Debug("Received message {MessageID} on channel {ChannelID} of type {MessageType} with content {Content}",
                message.ID, message.ChannelID, message.Type, message.Content.GetType());

            if (_messageHandlers.TryGetValue(message.ChannelID, out var registration) && registration.Mode == MessageDispatchMode.Inline)
            {
                registration.Handler.HandleMessage(this, message);
            }
            else
            {
                _manager.EnqueueForDispatch(this, message);
            }
        }

        /// <summary>
        /// Dispatching thread: deliver a queued message to its channel's handler, or to <see cref="IncomingMessages"/>.
        /// </summary>
        internal void DeliverQueuedMessage(NetworkMessage message)
        {
            if (_messageHandlers.TryGetValue(message.ChannelID, out var registration))
            {
                try
                {
                    registration.Handler.HandleMessage(this, message);
                }
                catch (Exception e)
                {
                    Log.Logger.Error(e, "Failed to handle message {MessageID} on channel {ChannelID}", message.ID, message.ChannelID);
                }
            }
            else
            {
                IncomingMessages.Enqueue(message);
            }
        }

        /// <summary>
        /// Network thread: one transport tick.
        /// </summary>
        internal void Update()
        {
            DrainPendingOutgoing();

            var now = DateTime.UtcNow;
            var settings = _manager.Settings;

            if (_connectRequestPending)
            {
                _connectRequestPending = false;
                LastConnectionAttempt = now;
                SendConnectRequest();
            }

            if (State == ConnectionState.Started && LastConnectionAttempt.AddSeconds(3) < now)
            {
                LastConnectionAttempt = now;
                _connectionRetryCount++;
                if (_connectionRetryCount > 5)
                {
                    TryTransitionState(ConnectionState.Started, ConnectionState.Disabled);
                    Log.Logger.Error("Failed to connect to peer");
                    return;
                }
                if (_isOutgoing)
                {
                    SendConnectRequest();
                    Log.Logger.Warning("Retrying sending connection request to {0}", RemoteEndpoint);
                }
            }

            if (LastPing + settings.PingInterval < now)
            {
                LastPing = now;
                RandomPingNumber = _rng.Next();
                Ping p = new Ping() { RandomNumber = RandomPingNumber };
                EnqueueInternalMessage(MessageType.Priority, p);
            }

            if (LastReceivedPong + settings.PongTimeout < now && TryTransitionState(ConnectionState.Connected, ConnectionState.Disconnected))
            {
                Log.Logger.Warning("Lost connection {0}, trying to reconnect", ConnectionId);
            }

            // check for messages that need to be resent
            foreach (var sent in _trackedSentMessages.Values)
            {
                var message = sent.Message;
                if (message.State != MessageState.Sent)
                {
                    continue;
                }

                if (sent.IsSplit)
                {
                    // a split message is delivered by its fragments; give up when they stop making progress
                    if (now > sent.LastProgress + settings.PongTimeout)
                    {
                        Log.Debug("Failed to deliver split message {MessageID}", message.ID);
                        message.MarkFailed();
                        foreach (var fragment in sent.SentFragmentMessages)
                        {
                            if (fragment.State != MessageState.Delivered)
                            {
                                fragment.MarkFailed();
                            }
                        }
                    }
                    continue;
                }

                if (now > sent.LastSent + GetResendInterval(message.Type))
                {
                    if (sent.SplitParent != null && sent.SplitParent.Message.State is MessageState.Delivered or MessageState.Failed)
                    {
                        // the whole message was settled; nothing left to do for this fragment
                        continue;
                    }

                    // fragments keep retrying until their split message times out as a whole
                    if (sent.ResendCount > 3 && sent.SplitParent == null) //failed to deliver message
                    {
                        Log.Debug("Failed to deliver message {MessageID}", message.ID);
                        message.MarkFailed();
                        continue;
                    }
                    Log.Debug("Resending message {MessageID}", message.ID);
                    // resend the message
                    sent.LastSent = now;
                    sent.ResendCount++;
                    if (message.Type.HasFlag(MessageType.Priority))
                    {
                        _outgoingPriorityMessages.AddLast(message);
                    }
                    else
                    {
                        _outgoingNonPriorityMessages.AddLast(message);
                    }
                }
            }

            // remove messages that have been delivered or failed, and fragments of settled split messages
            _trackedSentMessages.RemoveAll((k, v) =>
                v.Message.State is MessageState.Delivered or MessageState.Failed ||
                v.SplitParent?.Message.State is MessageState.Delivered or MessageState.Failed);

            // send messages
            SendMessages();
        }

        private void DrainPendingOutgoing()
        {
            List<NetworkMessage> batch;
            lock (_pendingOutgoingLock)
            {
                if (_pendingOutgoing.Count == 0)
                {
                    return;
                }
                batch = _pendingOutgoing;
                _pendingOutgoing = new List<NetworkMessage>();
            }

            foreach (var message in batch)
            {
                AddToOutgoing(message);
            }
        }

        /// <summary>
        /// Any thread: stop accepting messages and fail the ones not yet handed to the network thread.
        /// </summary>
        internal void MarkClosed()
        {
            List<NetworkMessage> pending;
            lock (_pendingOutgoingLock)
            {
                _closed = true;
                pending = _pendingOutgoing;
                _pendingOutgoing = new List<NetworkMessage>();
            }

            foreach (var message in pending)
            {
                message.MarkFailed();
            }
        }

        /// <summary>
        /// Network thread (or after it stopped): fail everything still awaiting delivery.
        /// </summary>
        internal void FailOutstandingMessages()
        {
            MarkClosed();
            foreach (var sent in _trackedSentMessages.Values)
            {
                if (sent.Message.State is not (MessageState.Delivered or MessageState.Failed))
                {
                    sent.Message.MarkFailed();
                }
            }
            _trackedSentMessages.Clear();
            _outgoingPriorityMessages.Clear();
            _outgoingNonPriorityMessages.Clear();
        }

        /// <summary>
        /// compute the resend interval based on the average ping and reasonable lower and upper bounds
        /// </summary>
        public TimeSpan GetResendInterval(MessageType type)
        {
            float multiplier = 1;
            if (type.HasFlag(MessageType.Priority))
            {
                multiplier = 0.75f;
            }
            if (type.HasFlag(MessageType.Bulk))
            {
                multiplier = 3f;
            }
            var resendInterval = (int)(AveragePing * 3f) + Random.Shared.Next(0, 20); //rtt + some extra time

            resendInterval = Math.Clamp(resendInterval, 120, 500); // clamp to reasonable values

            return TimeSpan.FromMilliseconds(resendInterval) * multiplier;
        }

        private void SendMessages()
        {
            var key = _sharedKey;
            if (PeerPublicKey == null || key == null)
            {
                return;
            }

            var settings = _manager.Settings;
            // account for encryption padding and other overhead
            var maxPacketSize = settings.MaximumTransmissionUnit - 128;
            int totalBytesSent = 0;

            while ((_outgoingPriorityMessages.Count > 0 || _outgoingNonPriorityMessages.Count > 0) && totalBytesSent < settings.MaxBytesSentPerUpdate)
            {
                // prepare the packet
                var iv = _manager.GenerateInitializationVector();

                var ms = new MemoryStream();
                // write the initialization vector for the packet AES encryption
                ms.Write(iv);

                using var aes = _manager.CreateAes(key, iv);
                using var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: true);
                var writer = new BinaryWriter(cs);
                // write packet preamble
                writer.Write(0x12345678);

                int messageCount = 0;
                var currentPacketSize = 16 + 4; //initialization vector + preamble

                FillPacket(_outgoingPriorityMessages, maxPacketSize, writer, ref currentPacketSize, ref messageCount, ref totalBytesSent);
                FillPacket(_outgoingNonPriorityMessages, maxPacketSize, writer, ref currentPacketSize, ref messageCount, ref totalBytesSent);

                if (messageCount == 0)
                {
                    // everything left was skipped (settled) or split into fragments queued behind
                    continue;
                }

                // flush the streams
                writer.Flush();
                cs.FlushFinalBlock();

                Log.Debug("Sending packet with {PacketCount} messages with size of {Size}", messageCount, ms.Length);

                _manager.SendPacket(ms.ToArray(), RemoteEndpoint, ConnectionId);
            }
        }

        private void FillPacket(LinkedList<NetworkMessage> queue, int maxPacketSize, BinaryWriter writer, ref int currentPacketSize,
            ref int messageCount, ref int totalBytesSent)
        {
            var settings = _manager.Settings;
            while (queue.Count > 0)
            {
                var message = queue.First!.Value;

                if (message.State is MessageState.Delivered or MessageState.Failed)
                {
                    queue.RemoveFirst();
                    continue;
                }

                var content = message.SerializedContent!;

                if (content.Length > settings.MaximumTransmissionUnit / 2)
                {
                    queue.RemoveFirst();
                    Log.Logger.Information("Message {MessageID} is Larger than the MTU. Splitting...", message.ID);
                    SplitMessage(message, content);
                    continue;
                }

                var entrySize = content.Length + 8 + 8 + 1; // message id + channel id + message type + content
                // packet full; a packet always takes at least one message so a tiny MTU setting cannot stall sending
                if (messageCount > 0 && currentPacketSize + entrySize > maxPacketSize)
                {
                    return;
                }

                queue.RemoveFirst();

                writer.Write(message.ID);
                writer.Write(message.ChannelID);
                writer.Write((byte)message.Type);
                writer.Write(content);
                messageCount++;

                message.State = MessageState.Sent;

                currentPacketSize += entrySize;
                totalBytesSent += entrySize;

                if (message.Type.HasFlag(MessageType.Reliable) && _trackedSentMessages.TryGetValue(message.ID, out var sent))
                {
                    sent.LastSent = DateTime.UtcNow;
                }

                Log.Debug("Sending message {MessageID} on channel {ChannelID} of type {MessageType} with content {Content}", message.ID, message.ChannelID, message.Type, message.Content.GetType());
            }
        }

        private void SplitMessage(NetworkMessage message, byte[] serializedMessage)
        {
            int contentSize = serializedMessage.Length;
            int fragmentSize = (int)(_manager.Settings.MaximumTransmissionUnit / 3f);
            int totalParts = (contentSize + fragmentSize - 1) / fragmentSize;

            _trackedSentMessages.TryGetValue(message.ID, out var parent);
            if (parent != null)
            {
                parent.IsSplit = true;
                parent.LastProgress = DateTime.UtcNow;
            }
            message.State = MessageState.Sent;

            // fragments are reordered by the network, so they must not be sequenced themselves; the reassembled
            // message keeps the original type and ID and is sequenced on its own channel
            var fragmentType = (message.Type & ~MessageType.Sequenced) | MessageType.Bulk;

            for (int i = 0; i < contentSize; i += fragmentSize)
            {
                SplitMessagePart part = new SplitMessagePart
                {
                    OriginalMessageId = message.ID,
                    Channel = message.ChannelID,
                    OriginalMessageType = message.Type,
                    TotalParts = totalParts,
                    PartNumber = i / fragmentSize,
                    PartSize = fragmentSize,
                    Data = serializedMessage.AsSpan(i, Math.Min(fragmentSize, contentSize - i)).ToArray()
                };

                var fragment = EnqueueInternalMessage(fragmentType, part, parent);
                parent?.SentFragmentMessages.Add(fragment);
            }
        }

        internal sealed class HandlerRegistration
        {
            public IMessageHandler Handler { get; }
            public MessageDispatchMode Mode { get; }

            public HandlerRegistration(IMessageHandler handler, MessageDispatchMode mode)
            {
                Handler = handler;
                Mode = mode;
            }
        }
    }
}
