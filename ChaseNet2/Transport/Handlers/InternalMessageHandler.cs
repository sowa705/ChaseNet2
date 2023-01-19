using System;
using System.Threading.Tasks;
using ChaseNet2.Transport.Messages;
using Serilog;

namespace ChaseNet2.Transport
{
    public partial class Connection
    {
        public class InternalMessageHandler:IMessageHandler
        {
            public void HandleMessage(Connection connection, NetworkMessage message)
            {
                switch (message.Content)
                {
                    case Ack ack:
                        var sentMessage = connection._trackedSentMessages[ack.MessageID];
                        if (sentMessage != null)
                        {
                            if (sentMessage.Message.State == MessageState.Delivered)
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
                            sentMessage.Message.State = MessageState.Delivered;
                            if (sentMessage.DeliveryTask!=null)
                            {
                                sentMessage.DeliveryTask.SetResult(true);
                            }
                        }
                        break;
                    case Ping ping:
                        var p = new Pong
                        {
                            RandomNumber = ping.RandomNumber
                        };
                        connection.EnqueueInternalMessage(MessageType.Unreliable, p);
                        break;
                    case Pong pong:
                        if (pong.RandomNumber == connection.RandomPingNumber)
                        {
                            // we got a valid pong
                            var pingTime = (DateTime.UtcNow - connection.LastPing)/2; // ping is half of round trip time
                                    
                            connection.AveragePing = (connection.AveragePing + (float) pingTime.TotalMilliseconds) / 2; // simple moving average
                            connection.LastReceivedPong = DateTime.UtcNow;
                                    
                            connection.State = ConnectionState.Connected; // ping came back so obviously we are connected
                        }
                        break;
                    case SplitMessagePart splitMessagePart:
                        if (!connection._splitReceivedMessages.ContainsKey(splitMessagePart.OriginalMessageId))
                        {
                            connection._splitReceivedMessages.Add(splitMessagePart.OriginalMessageId, new SplitReceivedMessage(splitMessagePart));
                        }
                        
                        var splitMessage = connection._splitReceivedMessages[splitMessagePart.OriginalMessageId];
                        Log.Debug("Added part {part} of {total} to split message {id}", splitMessagePart.PartNumber, splitMessagePart.TotalParts, splitMessagePart.OriginalMessageId);
                        splitMessage.AddPart(splitMessagePart);

                        if (splitMessage.IsComplete())
                        {
                            Log.Debug("Split message complete");
                            var networkMessage = splitMessage.GetCompleteMessage(connection._manager.Serializer);
                            
                            connection.RouteReceivedMessage(networkMessage);
                        }
                        break;
                    default:
                        Console.WriteLine($"Unknown internal message type: {message.Content}");
                        break;
                }
            }
        }
    }
}