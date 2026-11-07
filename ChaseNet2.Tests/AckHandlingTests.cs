using ChaseNet2.Transport;
using ChaseNet2.Transport.Messages;
using Xunit;

namespace ChaseNet2.Tests;

public class AckHandlingTests
{
    [Fact]
    public async Task AckForUnknownMessageIsIgnored()
    {
        var (_, _, connection) = await ConnectionManagerTests.GetConnectedManagers();
        var handler = connection.MessageHandlers[(ulong)InternalChannelType.ConnectionInternal];

        // A late or duplicate Ack names a message that was already delivered, failed and cleaned up, or never tracked.
        var ack = new NetworkMessage(1, (ulong)InternalChannelType.ConnectionInternal, MessageType.Unreliable,
            new Ack { MessageID = ulong.MaxValue - 1 });

        var exception = Record.Exception(() => handler.HandleMessage(connection, ack));

        Assert.Null(exception);
    }

    [Fact]
    public async Task DuplicateAckForDeliveredMessageIsIgnored()
    {
        var (first, second, connection) = await ConnectionManagerTests.GetConnectedManagers();
        var handler = connection.MessageHandlers[(ulong)InternalChannelType.ConnectionInternal];

        var message = connection.EnqueueMessage(MessageType.Reliable, 1000, new DummyMessage(64));
        await Helpers.UpdateUntil(() => message.State == MessageState.Delivered, first, second);
        Assert.Equal(MessageState.Delivered, message.State);

        // The delivered message has been removed from tracking; a second Ack for it must not throw.
        var ack = new NetworkMessage(2, (ulong)InternalChannelType.ConnectionInternal, MessageType.Unreliable,
            new Ack { MessageID = message.ID });

        var exception = Record.Exception(() => handler.HandleMessage(connection, ack));

        Assert.Null(exception);
    }
}
