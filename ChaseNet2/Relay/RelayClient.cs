using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using ChaseNet2.Transport;
using Serilog;

namespace ChaseNet2.Relay
{
    public class RelayClient: ConnectionHandler,IMessageHandler
    {
        public List<(Connection, RelayAdvertisement)> ReceivedAdvertisements;

        public override Task OnHandlerAttached(ConnectionManager manager)
        {
            return Task.CompletedTask;
        }

        public override Task OnConnectionAttached(Connection connection)
        {
            connection.RegisterMessageHandler((ulong)InternalChannelType.Relay,this);
            return Task.CompletedTask;
        }

        public override void OnConnectionUpdated(Connection connection)
        {
        }

        public override void OnManagerUpdated()
        {
        }

        public void HandleMessage(Connection connection, NetworkMessage message)
        {
            if (!(message.Content is RelayAdvertisement advert))
            {
                return;
            }

            if (ReceivedAdvertisements.Exists(x=>x.Item1==connection))
            {
                return;
            }
            
            Log.Information("Received a relay advertisement from {connection}", connection.ConnectionId);
            ReceivedAdvertisements.Add((connection,advert));
        }
    }
}