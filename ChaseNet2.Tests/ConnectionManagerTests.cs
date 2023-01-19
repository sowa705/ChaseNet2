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
    static async Task UpdateManagers(params ConnectionManager[] managers)
    {
        for (int i = 0; i < 100; i++)
        {
            await Task.Delay(10);
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
    [InlineData(1024, true)]
    [InlineData(1024 * 1024, true)] // 1 MB, should be fragmented
    [InlineData(1024 * 1024 * 16, false)] // Over the default MTU
    public async Task ConnectionManagerShouldSendPacket(int packetSize, bool shouldPass)
    {
        // Arrange
        var (first, second, connection) = await GetConnectedManagers();
        first.Serializer.RegisterType(typeof(DummyMessage));
        second.Serializer.RegisterType(typeof(DummyMessage));
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
}