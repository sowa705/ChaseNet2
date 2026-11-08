using System;
using ChaseNet2.Transport.Messages;
using Serilog;

namespace ChaseNet2.Transport
{
    public partial class Connection
    {
        /// <summary>
        /// Transport messages (acks, ping/pong, split message parts). Registered inline: runs on the network thread.
        /// </summary>
        public class InternalMessageHandler : IMessageHandler
        {
            public void HandleMessage(Connection connection, NetworkMessage message)
            {
                switch (message.Content)
                {
                    case Ack ack:
                        HandleAck(connection, ack);
                        break;
                    case Ping ping:
                        var p = new Pong
                        {
                            RandomNumber = ping.RandomNumber
                        };
                        connection.EnqueueInternalMessage(MessageType.Priority, p);
                        break;
                    case Pong pong:
                        if (pong.RandomNumber == connection.RandomPingNumber)
                        {
                            // we got a valid pong
                            var now = DateTime.UtcNow;
                            var pingTime = (now - connection.LastPing) / 2; // ping is half of round trip time

                            connection.AveragePing = (connection.AveragePing + (float)pingTime.TotalMilliseconds) / 2; // simple moving average
                            connection.LastReceivedPong = now;

                            connection.MarkAlive(); // ping came back so obviously we are connected
                        }
                        break;
                    case SplitMessagePart splitMessagePart:
                        HandleSplitMessagePart(connection, splitMessagePart);
                        break;
                    default:
                        Log.Logger.Warning("Unknown internal message type: {Content}", message.Content);
                        break;
                }
            }

            private static void HandleAck(Connection connection, Ack ack)
            {
                // The message may already be gone: delivered (duplicate/late ack), failed after too many
                // resends, or never tracked at all. None of those is an error worth crashing the host for.
                if (!connection._trackedSentMessages.TryGetValue(ack.MessageID, out var sentMessage))
                {
                    return;
                }

                if (sentMessage.Message.State is MessageState.Delivered or MessageState.Failed)
                {
                    return;
                }

                if (sentMessage.IsSplit)
                {
                    foreach (var fragment in sentMessage.SentFragmentMessages)
                    {
                        fragment.State = MessageState.Delivered;
                    }
                }
                sentMessage.Message.MarkDelivered();

                var parent = sentMessage.SplitParent;
                if (parent != null && parent.Message.State == MessageState.Sent)
                {
                    // every fragment acknowledged means the peer holds the whole message, even if its ack for the
                    // reassembled message gets lost
                    parent.LastProgress = DateTime.UtcNow;
                    parent.DeliveredFragmentCount++;
                    if (parent.DeliveredFragmentCount >= parent.SentFragmentMessages.Count)
                    {
                        parent.Message.MarkDelivered();
                    }
                }
            }

            private static void HandleSplitMessagePart(Connection connection, SplitMessagePart part)
            {
                if (connection._completedSplitMessageIds.Contains(part.OriginalMessageId))
                {
                    return; // late resend of a fragment of a message we already reassembled
                }

                if (part.TotalParts <= 0 || part.PartSize <= 0 || part.PartNumber < 0 || part.PartNumber >= part.TotalParts ||
                    (long)part.TotalParts * part.PartSize > connection._manager.Settings.MaxMessageLength + part.PartSize)
                {
                    Log.Logger.Warning("Received an invalid split message part for message {id}", part.OriginalMessageId);
                    return;
                }

                if (!connection._splitReceivedMessages.TryGetValue(part.OriginalMessageId, out var splitMessage))
                {
                    splitMessage = new SplitReceivedMessage(part);
                    connection._splitReceivedMessages.Add(part.OriginalMessageId, splitMessage);
                }
                else
                {
                    splitMessage.AddPart(part);
                }

                Log.Debug("Added part {part}  ({partCount}/{total}) to split message {id}", part.PartNumber, splitMessage.ReceivedParts.Count, part.TotalParts, part.OriginalMessageId);

                if (splitMessage.IsComplete())
                {
                    Log.Debug("Split message complete");
                    connection._splitReceivedMessages.Remove(part.OriginalMessageId);
                    connection._completedSplitMessageIds.Add(part.OriginalMessageId);

                    var networkMessage = splitMessage.GetCompleteMessage(connection._manager.Serializer);
                    connection.RouteReceivedMessage(networkMessage);
                }
            }
        }
    }
}
