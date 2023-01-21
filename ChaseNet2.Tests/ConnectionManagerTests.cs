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
    /// Run a few update cycles asynchronously
    /// </summary>
    /// <param name="managers"></param>
    public static async Task UpdateManagers(params ConnectionManager[] managers)
    {
        for (int i = 0; i < 200; i++)
        {
            await Task.Delay(5);
            foreach (var manager in managers)
            {
                await manager.Update();
            }
        }
    }

    /// <summary>
    /// Helper method that creates two managers and connects them
    /// </summary>
    /// <returns></returns>
    public static async Task<(ConnectionManager, ConnectionManager, Connection)> GetConnectedManagers()
    {
        int port = Random.Shared.Next(10000, 20000);
        var first = new ConnectionManager(port);
        first.AcceptNewConnections = true;
        var second = new ConnectionManager();
        
        first.Serializer.RegisterType(typeof(DummyMessage));
        second.Serializer.RegisterType(typeof(DummyMessage));
        
        var connection = second.CreateConnection(IPEndPoint.Parse($"127.0.0.1:{port}"));
        await UpdateManagers(first, second);
        
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
    [InlineData(48000, true)] // first size that will be fragmented
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
        await UpdateManagers(first, second);

        // Assert

        if (!shouldPass)
        {
            Assert.True(message.State == MessageState.Failed);
            Assert.True(connection2.IncomingMessages.Count == 0);
            return;
        }
        
        var receivedMessage = connection2.IncomingMessages.FirstOrDefault();
        var receivedContent = (DummyMessage) receivedMessage.Content;
        Assert.Equal(shouldPass, receivedContent == messageContent);
    }
    
    [Theory]
    [InlineData(0.0f)]
    [InlineData(0.1f)]
    [InlineData(0.2f)] // 20% is the max "supported" packet loss. In some cases it can be higher but it's not recommended
    public async Task ConnectionShouldSendReliableMessageWithPacketLoss(float packetLoss)
    {
        // Arrange
        var (first, second, connection) = await GetConnectedManagers();
        first.Settings.SimulatedPacketLoss = packetLoss;
        second.Settings = first.Settings;
        var connection2 = first.Connections.First();

        // Act
        DummyMessage messageContent = new DummyMessage(1024*1024*2);
        var message = connection.EnqueueMessage(MessageType.Reliable, 1000, messageContent);
        int maxUpdates = 0;
        while (!(message.State == MessageState.Failed || message.State == MessageState.Delivered) && maxUpdates < 10)
        {
            Log.Debug("Loop {0}", maxUpdates);
            await UpdateManagers(first, second);
            maxUpdates++;
        }

        // Assert
        var receivedMessage = connection2.IncomingMessages.First();
        var receivedContent = (DummyMessage) receivedMessage.Content;
        Assert.True(receivedContent == messageContent);
        Assert.True(message.State == MessageState.Delivered);
    }
}