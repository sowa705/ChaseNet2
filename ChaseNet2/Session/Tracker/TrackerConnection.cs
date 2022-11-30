using System;
using System.Threading.Tasks;
using ChaseNet2.Session.Messages;
using ChaseNet2.Transport;
using Serilog;

namespace ChaseNet2.Session
{
    public class TrackerConnection
    {
        public SessionTracker SessionTracker { get; set; }
        public Connection Connection { get; set; }
        public TrackerConnectionState State { get; set; }
        
        private NetworkMessage _joinSessionResponse;
        
        private NetworkMessage _sessionUpdateMessage;
        private DateTime _lastSessionUpdate;

        public void Update()
        {
            if (State==TrackerConnectionState.Connected)
            {
                if (_lastSessionUpdate+TimeSpan.FromSeconds(3)<DateTime.UtcNow) // send session update every second
                {
                    _lastSessionUpdate = DateTime.UtcNow;
                    
                    SessionUpdate sessionUpdate = new SessionUpdate();

                    foreach (var con in SessionTracker.Connections)
                    {
                        sessionUpdate.Peers.Add(new ConnectionTarget()
                        {
                            // we compute a somewhat unique id for each connection
                            // this is used to identify the connection on the other side and must be the same for both peers
                            ConnectionId = con.Connection.ConnectionId^Connection.ConnectionId,
                            EndPoint = con.Connection.RemoteEndpoint,
                            PublicKey = con.Connection.PeerPublicKey
                        });
                    }
                    
                    // send the update to the connection
                    
                    _sessionUpdateMessage = Connection.EnqueueMessage(MessageType.Reliable, (ulong) InternalChannelType.TrackerInternal, sessionUpdate);
                }

                if (Connection.State==ConnectionState.Disconnected)
                {
                    State = TrackerConnectionState.LostConnection;
                }
            }
        }

        public async Task HandleNewConnection()
        {
            Log.Information("New connection from {remote}", Connection.RemoteEndpoint);
            var message=await Connection.WaitForChannelMessageAsync((ulong)InternalChannelType.SessionJoin, TimeSpan.FromSeconds(5));

            var joinRequest = message.Content as JoinSession;

            if (joinRequest.SessionName!=SessionTracker.SessionName)
            {
                throw new Exception("Client tried to join wrong session");
            }
            
            // send join response
            JoinSessionResponse joinResponse = new JoinSessionResponse();
            joinResponse.Accepted = true;
            
            _joinSessionResponse = Connection.EnqueueMessage(MessageType.Reliable, (ulong) InternalChannelType.SessionJoin, joinResponse);
            await Connection.WaitForDeliveryAsync(_joinSessionResponse);
            State = TrackerConnectionState.Connected;
            Log.Logger.Information("Connection {0} joined session {1}", Connection.ConnectionId, SessionTracker.SessionName);
        }
    }
}