using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using ChaseNet2.Transport;
using Serilog;

namespace ChaseNet2.Relay
{
    /// <summary>
    /// Relay host acts as an intermediate for peers that cannot establish connection between themselves.
    /// </summary>
    public class RelayHost : ConnectionHandler, IMessageHandler, IUnknownConnectionHandler
    {
        public List<(ulong connectionID, IPEndPoint endPointA, IPEndPoint endPointB)> RelayedConnections;
        public int MaxRelayedConnections = 4;
        bool CheckIPEndPoint(ConnectionManager manager, IPEndPoint endPoint)
        {
            foreach (var connection in manager.Connections)
            {
                if (connection.RemoteEndpoint.Address == endPoint.Address)
                {
                    return true;
                }
            }

            return false;
        }
        
        public void HandleMessage(Connection connection, NetworkMessage message)
        {
            if (!(message.Content is RelayRequest request))
            {
                return;
            }

            if (!CheckIPEndPoint(connection.Manager,request.TargetEndPoint))
            {
                Log.Warning("Connection {connection} tried to relay message to an unknown endpoint {endpoint}", connection.ConnectionId, request.TargetEndPoint);
            }
            
            connection.Manager.SendPacket(request.MessageContent, request.TargetEndPoint, request.TargetConnectionID);
        }

        public override Task OnHandlerAttached(ConnectionManager manager)
        {
            return Task.CompletedTask;
        }

        public override Task OnConnectionAttached(Connection connection)
        {
            var preferredMtu = (ushort) Math.Clamp(256, 32768, connection.Manager.Settings.MaximumTransmissionUnit / 2);
            var advertisement = new RelayAdvertisement() { PreferredMTU = preferredMtu };
            connection.EnqueueMessage(MessageType.Reliable, (ulong)InternalChannelType.Relay, advertisement);
            
            return Task.CompletedTask;
        }

        public override void OnConnectionUpdated(Connection connection)
        {
        }

        public override void OnManagerUpdated()
        {
        }

        public void OnMessageFromUnknownConnectionReceived(ulong connectionID, Stream dataStream)
        {
            
        }
    }

    public class RelayConnection
    {
        public IPEndPoint EndPointA;
        public IPEndPoint EndPointB;
        public ulong ConnectionID;

        public ulong SentBytes;
        public ulong ReceivedBytes;
    }
}