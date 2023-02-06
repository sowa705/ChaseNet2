using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private ConnectionManager _manager;
        public List<RelayConnection> RelayedConnections;
        public int MaxRelayedConnections = 4;
        bool CheckIPEndPoint(IPEndPoint endPoint)
        {
            foreach (var connection in _manager.Connections)
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

            if (RelayedConnections.Count >= MaxRelayedConnections)
            {
                Log.Debug("Denying relay request because we are out of slots");
                return;
            }

            if (!CheckIPEndPoint(request.TargetEndPoint))
            {
                Log.Debug("Rejecting request to relay to an unknown endpoint");
                return;
            }

            var existingRelayConnection = RelayedConnections.FirstOrDefault(x => x.ConnectionID == request.TargetConnectionID);

            if (existingRelayConnection != null) //we already relay this connection. nothing to do
            {
                return;
            }

            var relayConnection = new RelayConnection
            {
                EndPointA = connection.RemoteEndpoint,
                EndPointB = request.TargetEndPoint,
                ConnectionID = request.TargetConnectionID,
                LastResponse = DateTime.UtcNow
            };
            
            RelayedConnections.Add(relayConnection);
        }

        public override Task OnHandlerAttached(ConnectionManager manager)
        {
            _manager = manager;
            _manager.UnknownConnectionHandler = this;
            return Task.CompletedTask;
        }

        public override Task OnConnectionAttached(Connection connection)
        {
            var preferredMtu = (ushort) Math.Clamp(256, 32768, connection.Manager.Settings.MaximumTransmissionUnit / 2);
            var advertisement = new RelayAdvertisement() { PreferredMTU = preferredMtu };
            connection.EnqueueMessage(MessageType.Reliable, (ulong)InternalChannelType.Relay, advertisement);
            
            connection.RegisterMessageHandler((ulong)InternalChannelType.Relay, this);
            
            return Task.CompletedTask;
        }

        public override void OnConnectionUpdated(Connection connection)
        {
        }

        public override void OnManagerUpdated()
        {
            RelayedConnections.RemoveAll(x => x.LastResponse < DateTime.UtcNow - TimeSpan.FromSeconds(6));
        }

        public void OnMessageFromUnknownConnectionReceived(ulong connectionID, IPEndPoint endPoint, Stream dataStream)
        {
            var connection = RelayedConnections.FirstOrDefault(x => x.ConnectionID == connectionID);

            if (connection == null)
            {
                return;
            }

            var otherEp = connection.EndPointA.Equals(endPoint) ? connection.EndPointB : connection.EndPointA;
            var data = new byte[dataStream.Length-8]; //remove prefix
            var readBytes = dataStream.Read(data);

            if (readBytes != data.Length)
            {
                throw new Exception("Cannot read from the stream");
            }

            connection.RelayedBytes += (uint)data.Length;
            connection.LastResponse = DateTime.UtcNow;

            _manager.SendPacket(data, otherEp, connectionID);
        }
    }

    public class RelayConnection
    {
        public IPEndPoint EndPointA;
        public IPEndPoint EndPointB;
        public ulong ConnectionID;

        public ulong RelayedBytes;
        public DateTime LastResponse;
    }
}