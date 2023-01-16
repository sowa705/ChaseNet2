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
                        var sentMessage = connection._trackedSentMessages.Find(m => m.Message.ID == ack.MessageID);
                        if (sentMessage != null)
                        {
                            if (sentMessage.Message.State == MessageState.Delivered)
                            {
                                return;
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
                    default:
                        Console.WriteLine($"Unknown internal message type: {message.Content}");
                        break;
                }
            }
        }
    }
}