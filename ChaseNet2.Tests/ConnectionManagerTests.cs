using System.Net;
using ChaseNet2.Transport;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;
using Xunit.Abstractions;

namespace ChaseNet2.Tests;

public class ConnectionManagerTests
{
    public ConnectionManagerTests(ITestOutputHelper output)
    {
        Logger logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.TestOutput(output, LogEventLevel.Debug)
            .CreateLogger();

        Log.Logger = logger;
    }

    /// <summary>
    /// Helper method that creates two managers and connects them
    /// </summary>
    /// <returns></returns>
    public static async Task<(ConnectionManager, ConnectionManager, Connection)> GetConnectedManagers()
    {
        int port = Random.Shared.Next(10000, 20000);
        var first = new ConnectionManager(port);
        first.Settings.AcceptNewConnections = true;
        var second = new ConnectionManager();

        first.Serializer.RegisterType(typeof(DummyMessage));
        second.Serializer.RegisterType(typeof(DummyMessage));

        var connection = second.CreateConnection(IPEndPoint.Parse($"127.0.0.1:{port}"));
        await Helpers.UpdateUntil(() => connection.State == ConnectionState.Connected && first.Connections.Count == 1, first, second);

        return (first, second, connection);
    }

    [Fact]
    public async Task ConnectionManagerShouldConnect()
    {
        // Arrange & Act
        var (first, second, connection) = await GetConnectedManagers();

        // Assert
        Assert.True(connection.State == ConnectionState.Connected);
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(1024 * 1024 + 1, true)] // Weird size that will be fragmented
    [InlineData(1024 * 1024 * 16, false)] // Over the default MTU
    public async Task ConnectionShouldSendReliableMessage(int packetSize, bool shouldPass)
    {
        // Arrange
        var (first, second, connection) = await GetConnectedManagers();
        var connection2 = first.Connections.First();

        // Act
        DummyMessage messageContent = new DummyMessage(packetSize);
        var message = connection.EnqueueMessage(MessageType.Reliable, 1000, messageContent);
        await Helpers.UpdateUntil(() => message.State is MessageState.Delivered or MessageState.Failed, first, second);

        // Assert

        if (!shouldPass)
        {
            Assert.True(message.State == MessageState.Failed);
            Assert.True(connection2.IncomingMessages.Count == 0);
            return;
        }

        var receivedMessage = connection2.IncomingMessages.FirstOrDefault();
        var receivedContent = receivedMessage?.As<DummyMessage>();
        Assert.NotNull(receivedContent);
        Assert.Equal(shouldPass, receivedContent! == messageContent);
    }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(0.1f)]
    public async Task ConnectionShouldSendReliableMessageWithPacketLoss(float packetLoss)
    {
        // Arrange
        var (first, second, connection) = await GetConnectedManagers();
        first.Settings.SimulatedPacketLoss = packetLoss;
        second.Settings = first.Settings;
        var connection2 = first.Connections.First();

        // Act
        DummyMessage messageContent = new DummyMessage(1024 * 1024 * 2);
        var message = connection.EnqueueMessage(MessageType.Reliable, 1000, messageContent);
        await Helpers.UpdateUntil(() => message.State is MessageState.Delivered or MessageState.Failed,
            TimeSpan.FromSeconds(30), first, second);

        // Assert
        var receivedMessage = connection2.IncomingMessages.First();
        var receivedContent = (DummyMessage)receivedMessage.Content;
        Assert.True(receivedContent == messageContent);
        Assert.True(message.State == MessageState.Delivered);
    }
    
    [Fact]
    public async Task ConnectionShouldStayConnectedDuringContinuousExchange()
    {
        // Arrange: get two connected managers
        var (first, second, connFromSecond) = await GetConnectedManagers();
        var connFromFirst = first.Connections.First();

        const int updatesPerSecond = 60;
        const int totalSeconds = 5; // pings go out every 2 s, so this covers two full ping/pong exchanges
        int totalIterations = updatesPerSecond * totalSeconds;

        // The accepting side stays in Started until its first Pong arrives (pings go out every 2 s)
        Assert.True(await Helpers.UpdateUntil(
            () => connFromFirst.State == ConnectionState.Connected && connFromSecond.State == ConnectionState.Connected,
            TimeSpan.FromSeconds(6), first, second));

        // Act & Assert: run the loop, exchanging 1-byte messages each tick
        for (int i = 0; i < totalIterations; i++)
        {
            // enqueue a tiny message in both directions
            var msgA = connFromFirst.EnqueueMessage(MessageType.Reliable, 1000, new DummyMessage(1));
            var msgB = connFromSecond.EnqueueMessage(MessageType.Reliable, 1000, new DummyMessage(1));

            // run one tick (both managers); Helpers.UpdateManagers would run 80 ticks of 10 ms each
            await Task.Delay(1000 / updatesPerSecond);
            await first.Update();
            await second.Update();

            // fail immediately if either side disconnects
            Assert.Equal(ConnectionState.Connected, connFromFirst.State);
            Assert.Equal(ConnectionState.Connected, connFromSecond.State);
        }

        // (Optionally) verify that at least one message has been received end‑to‑end
        Assert.NotEmpty(connFromFirst.IncomingMessages);
        Assert.NotEmpty(connFromSecond.IncomingMessages);
    }

}