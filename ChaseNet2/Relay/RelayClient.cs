using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ChaseNet2.Transport;
using Serilog;

namespace ChaseNet2.Relay
{
    public class RelayClient: ConnectionHandler, IMessageHandler
    {
        public List<(Connection connection, RelayAdvertisement advertisement)> ReceivedAdvertisements;

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

            if (ReceivedAdvertisements.Exists(x=>x.connection==connection))
            {
                return;
            }
            
            Log.Information("Received a relay advertisement from {connection}", connection.ConnectionId);
            ReceivedAdvertisements.Add((connection,advert));
        }

        public async Task<bool> RequestRelay(Connection connection)
        {
            if (ReceivedAdvertisements.Count == 0)
            {
                return false;
            }

            var relayConnection = ReceivedAdvertisements.OrderBy(x => x.connection.AveragePing).First();

            var request = new RelayRequest
            {
                TargetEndPoint = connection.RemoteEndpoint,
                TargetConnectionID = connection.ConnectionId
            };

            var requestMessage = relayConnection.connection.EnqueueMessage(MessageType.Reliable, (ulong)InternalChannelType.Relay, request);

            await relayConnection.connection.WaitForDeliveryAsync(requestMessage);

            return true;
        }
    }
}