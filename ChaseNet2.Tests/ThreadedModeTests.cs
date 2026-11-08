using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using ChaseNet2.Transport;
using Xunit;

namespace ChaseNet2.Tests;

/// <summary>
/// The manager running its own network thread while the "game" (test) thread enqueues and dispatches.
/// </summary>
public class ThreadedModeTests
{
    private const string NetworkThreadName = "ChaseNet2 network";
    private sealed class Pair : IDisposable
    {
        public required ConnectionManager Server { get; init; }
        public required ConnectionManager Client { get; init; }
        /// <summary>Client side of the link.</summary>
        public required Connection ClientConnection { get; init; }
        /// <summary>Server side of the link.</summary>
        public required Connection ServerConnection { get; init; }

        public void Dispose()
        {
            Client.Dispose();
            Server.Dispose();
        }
    }

    private static TransportSettings FastSettings() => new()
    {
        ReceiveBufferSize = 212992,
        PingInterval = TimeSpan.FromMilliseconds(100),
        PongTimeout = TimeSpan.FromSeconds(1),
        TargetUpdateRate = 60,
    };

    private static async Task<Pair> StartPair(TransportSettings? settings = null, Action<ConnectionManager>? configureServer = null)
    {
        var server = new ConnectionManager(settings ?? FastSettings(), 0);
        server.Settings.AcceptNewConnections = true;
        var client = new ConnectionManager(settings ?? FastSettings());

        server.Serializer.RegisterType(typeof(DummyMessage));
        client.Serializer.RegisterType(typeof(DummyMessage));
        configureServer?.Invoke(server);

        server.Start();
        client.Start();

        var connection = client.CreateConnection(new IPEndPoint(IPAddress.Loopback, server.LocalEndPoint!.Port));

        Assert.True(await WaitUntil(() =>
        {
            server.Dispatch();
            client.Dispatch();
            return connection.State == ConnectionState.Connected && server.Connections.Count == 1 &&
                   server.Connections[0].State == ConnectionState.Connected;
        }, TimeSpan.FromSeconds(10)), "connection was not established");

        return new Pair { Server = server, Client = client, ClientConnection = connection, ServerConnection = server.Connections[0] };
    }

    private static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > timeout)
            {
                return false;
            }
            await Task.Delay(5);
        }
        return true;
    }

    private static DummyMessage Numbered(int value, int size = 16)
    {
        var message = new DummyMessage(size);
        BitConverter.GetBytes(value).CopyTo(message.Data, 0);
        return message;
    }

    private static int NumberOf(NetworkMessage message) => BitConverter.ToInt32(message.As<DummyMessage>().Data, 0);

    private sealed class RecordingHandler : IMessageHandler
    {
        public readonly ConcurrentQueue<(NetworkMessage Message, int ThreadId, string? ThreadName)> Received = new();

        public void HandleMessage(Connection connection, NetworkMessage message)
        {
            Received.Enqueue((message, Environment.CurrentManagedThreadId, Thread.CurrentThread.Name));
        }
    }

    [Fact]
    public async Task ConnectionSurvivesDispatchStallLongerThanPongTimeout()
    {
        using var pair = await StartPair();
        var reliable = new RecordingHandler();
        var sequenced = new RecordingHandler();
        pair.ServerConnection.RegisterMessageHandler(1000, reliable);
        pair.ServerConnection.RegisterMessageHandler(2000, sequenced);

        var sent = new List<NetworkMessage>();
        for (int i = 0; i < 20; i++)
        {
            sent.Add(pair.ClientConnection.EnqueueMessage(MessageType.Reliable, 1000, Numbered(i)));
        }
        for (int i = 0; i < 50; i++)
        {
            pair.ClientConnection.EnqueueMessage(MessageType.Sequenced, 2000, Numbered(i));
        }

        // Neither side dispatches for 3x the pong timeout (a long map load on the game thread).
        var stall = pair.Server.Settings.PongTimeout * 3;
        var observedStates = new ConcurrentBag<ConnectionState>();
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < stall)
        {
            observedStates.Add(pair.ClientConnection.State);
            observedStates.Add(pair.ServerConnection.State);
            Thread.Sleep(20); // blocking on purpose: this thread is the stalled game thread
        }

        Assert.All(observedStates, state => Assert.Equal(ConnectionState.Connected, state));
        Assert.True(pair.ClientConnection.LastReceivedPong > DateTime.UtcNow - pair.Server.Settings.PongTimeout);
        Assert.True(pair.ServerConnection.LastReceivedPong > DateTime.UtcNow - pair.Server.Settings.PongTimeout);

        // Acks are handled on the network thread, so delivery completed while nobody dispatched.
        Assert.All(sent, message => Assert.Equal(MessageState.Delivered, message.State));
        Assert.Empty(reliable.Received);

        pair.Server.Dispatch();

        Assert.Equal(Enumerable.Range(0, 20), reliable.Received.Select(x => NumberOf(x.Message)).Order());
        // Sequenced messages coalesce while queued: only the newest is dispatched.
        var sequencedReceived = Assert.Single(sequenced.Received);
        Assert.Equal(49, NumberOf(sequencedReceived.Message));
    }

    [Fact]
    public async Task ConcurrentEnqueueFromManyThreadsDeliversEverySnapshot()
    {
        using var pair = await StartPair();
        var handler = new RecordingHandler();
        pair.ServerConnection.RegisterMessageHandler(1000, handler);

        const int threadCount = 8;
        const int perThread = 200;
        var sent = new ConcurrentBag<NetworkMessage>();

        var threads = Enumerable.Range(0, threadCount).Select(t => new Thread(() =>
        {
            // one content object per thread, mutated right after every enqueue: each message must carry the
            // bytes it had at enqueue time
            var content = new DummyMessage(16);
            for (int i = 0; i < perThread; i++)
            {
                BitConverter.GetBytes(t * perThread + i).CopyTo(content.Data, 0);
                sent.Add(pair.ClientConnection.EnqueueMessage(MessageType.Reliable, 1000, content));
                BitConverter.GetBytes(-1).CopyTo(content.Data, 0);
            }
        })).ToList();

        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        Assert.Equal(threadCount * perThread, sent.Select(x => x.ID).Distinct().Count());

        Assert.True(await WaitUntil(() =>
        {
            pair.Server.Dispatch();
            return handler.Received.Count >= threadCount * perThread;
        }, TimeSpan.FromSeconds(20)), $"received {handler.Received.Count} of {threadCount * perThread}");

        Assert.Equal(Enumerable.Range(0, threadCount * perThread), handler.Received.Select(x => NumberOf(x.Message)).Order());

        var results = await Task.WhenAll(sent.Select(x => pair.ClientConnection.WaitForDeliveryAsync(x)));
        Assert.True(results.All(r => r),
            $"{results.Count(r => !r)} messages failed. Client: {pair.Client.Statistics}. Server: {pair.Server.Statistics}");
    }

    [Fact]
    public async Task WaitForDeliveryAfterAckAlreadyArrivedCompletes()
    {
        using var pair = await StartPair();

        var message = pair.ClientConnection.EnqueueMessage(MessageType.Reliable, 1000, Numbered(1));
        // let the ack arrive (and the tracking entry be cleaned up) before anyone waits
        Assert.True(await WaitUntil(() => message.State == MessageState.Delivered, TimeSpan.FromSeconds(5)));
        await Task.Delay(200);

        var waited = pair.ClientConnection.WaitForDeliveryAsync(message);
        Assert.True(waited.IsCompleted);
        Assert.True(await waited);
    }

    [Fact]
    public async Task WaitForDeliveryInManualModeCompletes()
    {
        var (first, second, connection) = await ConnectionManagerTests.GetConnectedManagers();
        using var _ = first;
        using var __ = second;

        var message = connection.EnqueueMessage(MessageType.Reliable, 1000, new DummyMessage(64));
        var delivery = connection.WaitForDeliveryAsync(message);

        Assert.True(await Helpers.UpdateUntil(() => delivery.IsCompleted, first, second));
        Assert.True(await delivery);
    }

    [Fact]
    public async Task QueuedHandlersRunOnDispatchThreadInlineOnNetworkThread()
    {
        var connectHandler = new CallbackRecordingConnectionHandler();
        using var pair = await StartPair(configureServer: server => server.AttachHandler(connectHandler));

        // everything before this ran in StartPair's dispatch loop, never on the network thread
        Assert.NotEmpty(connectHandler.ConnectThreads);
        Assert.All(connectHandler.ConnectThreads, name => Assert.NotEqual(NetworkThreadName, name));
        while (connectHandler.UpdateThreads.TryDequeue(out _))
        {
        }

        var queued = new RecordingHandler();
        var inline = new RecordingHandler();
        pair.ServerConnection.RegisterMessageHandler(10, queued);
        pair.ServerConnection.RegisterMessageHandler(11, inline, MessageDispatchMode.Inline);

        pair.ClientConnection.EnqueueMessage(MessageType.Reliable, 10, Numbered(10));
        pair.ClientConnection.EnqueueMessage(MessageType.Reliable, 11, Numbered(11));

        // inline handlers run without anyone dispatching
        Assert.True(await WaitUntil(() => !inline.Received.IsEmpty, TimeSpan.FromSeconds(5)));
        Assert.Empty(queued.Received);

        // a dedicated "game thread" dispatches synchronously, so its identity is fixed
        const string gameThreadName = "game";
        var gameThread = new Thread(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            while (queued.Received.IsEmpty && stopwatch.Elapsed < TimeSpan.FromSeconds(5))
            {
                pair.Server.Dispatch();
                Thread.Sleep(5);
            }
        }) { Name = gameThreadName };
        gameThread.Start();
        gameThread.Join();

        var (_, _, queuedThreadName) = Assert.Single(queued.Received);
        var (_, _, inlineThreadName) = Assert.Single(inline.Received);
        Assert.Equal(gameThreadName, queuedThreadName);
        Assert.Equal(NetworkThreadName, inlineThreadName);

        // ConnectionHandler callbacks run on the dispatching thread too
        Assert.NotEmpty(connectHandler.UpdateThreads);
        Assert.All(connectHandler.UpdateThreads, name => Assert.Equal(gameThreadName, name));
    }

    [Fact]
    public async Task RemovedConnectionIsReportedOnDispatchThreadAndFailsPendingMessages()
    {
        using var pair = await StartPair();
        var removedOn = new ConcurrentBag<(ulong Id, int Thread)>();
        pair.Server.ConnectionRemoved += c => removedOn.Add((c.ConnectionId, Environment.CurrentManagedThreadId));

        var connection = pair.ServerConnection;
        pair.Server.RemoveConnection(connection.ConnectionId);

        Assert.Empty(pair.Server.Connections);
        Assert.Empty(removedOn);

        var late = connection.EnqueueMessage(MessageType.Reliable, 1000, Numbered(1));
        Assert.False(await connection.WaitForDeliveryAsync(late));

        pair.Server.Dispatch();
        Assert.Equal(new[] { (connection.ConnectionId, Environment.CurrentManagedThreadId) }, removedOn);
    }

    [Fact]
    public async Task LargeSequencedMessageIsReassembledOnItsChannel()
    {
        using var pair = await StartPair();
        var handler = new RecordingHandler();
        pair.ServerConnection.RegisterMessageHandler(3000, handler);

        var content = Numbered(7, size: 20_000);
        pair.ClientConnection.EnqueueMessage(MessageType.Sequenced, 3000, content);

        Assert.True(await WaitUntil(() =>
        {
            pair.Server.Dispatch();
            return !handler.Received.IsEmpty;
        }, TimeSpan.FromSeconds(10)));

        var received = Assert.Single(handler.Received);
        Assert.True(received.Message.As<DummyMessage>() == content);
    }

    [Fact]
    public async Task DisposeStopsNetworkThreadAndClosesSocket()
    {
        var server = new ConnectionManager(FastSettings(), 0);
        server.Settings.AcceptNewConnections = true;
        var port = server.LocalEndPoint!.Port;
        server.Start();
        Assert.True(server.IsRunning);

        // a connection that never completes its handshake keeps its reliable message pending
        var nobody = server.CreateConnection(new IPEndPoint(IPAddress.Loopback, 9));
        var pending = nobody.EnqueueMessage(MessageType.Reliable, 1000, Numbered(1));
        var delivery = nobody.WaitForDeliveryAsync(pending);
        await Task.Delay(100);

        server.Dispose();

        Assert.False(server.IsRunning);
        Assert.True(delivery.IsCompleted);
        Assert.False(await delivery);

        // the port is free again
        using (var rebound = new UdpClient(port))
        {
            Assert.Equal(port, ((IPEndPoint)rebound.Client.LocalEndPoint!).Port);
        }

        server.Dispose(); // idempotent
        Assert.Throws<ObjectDisposedException>(() => server.Start());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => server.Update());
        Assert.Equal(MessageState.Failed, nobody.EnqueueMessage(MessageType.Reliable, 1000, Numbered(2)).State);
    }

    [Fact]
    public async Task StopReturnsToManualMode()
    {
        using var pair = await StartPair();
        var handler = new RecordingHandler();
        pair.ServerConnection.RegisterMessageHandler(1000, handler);

        pair.Client.Stop();
        pair.Server.Stop();
        Assert.False(pair.Client.IsRunning);

        var message = pair.ClientConnection.EnqueueMessage(MessageType.Reliable, 1000, Numbered(5));
        Assert.True(await Helpers.UpdateUntil(() => message.State == MessageState.Delivered && !handler.Received.IsEmpty,
            pair.Client, pair.Server));
    }

    [Fact]
    public void RegisteringATypeTwiceReturnsTheSameId()
    {
        var manager = new ConnectionManager();
        using var _ = manager;
        Assert.Equal(manager.Serializer.RegisterType<DummyMessage>(), manager.Serializer.RegisterType<DummyMessage>());
    }

    private sealed class CallbackRecordingConnectionHandler : ConnectionHandler
    {
        public readonly ConcurrentQueue<string?> ConnectThreads = new();
        public readonly ConcurrentQueue<string?> UpdateThreads = new();

        public override Task OnAttached(ConnectionManager manager) => Task.CompletedTask;

        public override Task OnManagerConnect(Connection connection)
        {
            ConnectThreads.Enqueue(Thread.CurrentThread.Name);
            AddConnection(connection.ConnectionId);
            return Task.CompletedTask;
        }

        public override void ConnectionUpdate(Connection connection)
        {
            UpdateThreads.Enqueue(Thread.CurrentThread.Name);
        }

        public override void Update()
        {
        }
    }
}
