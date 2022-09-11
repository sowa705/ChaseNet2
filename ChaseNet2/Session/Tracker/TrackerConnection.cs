using System;
using ChaseNet2.Session.Messages;
using ChaseNet2.Transport;

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
            if (State==TrackerConnectionState.ReceivedJoinRequest)
            {
                HandleNewConnection();
                return;
            }

            if (State==TrackerConnectionState.SentJoinResponse)
            {
                if (_joinSessionResponse.State==MessageState.Delivered)
                {
                    State = TrackerConnectionState.Connected;
                    Console.WriteLine($"Connection {Connection.PeerPublicKey} joined session {SessionTracker.SessionName}");
                }
            }

            if (State==TrackerConnectionState.Connected)
            {
                if (_lastSessionUpdate+TimeSpan.FromSeconds(1)<DateTime.UtcNow) // send session update every second
                {
                    _lastSessionUpdate = DateTime.UtcNow;
                    
                    SessionUpdate sessionUpdate = new SessionUpdate();

                    foreach (var con in SessionTracker.Connections)
                    {
                        sessionUpdate.Peers.Add(new ConnectionTarget()
                        {
                            EndPoint = con.Connection.RemoteEndpoint,
                            PublicKey = con.Connection.PeerPublicKey
                        });
                    }
                    
                    // send the update to the connection
                    
                    _sessionUpdateMessage = Connection.EnqueueMessage(MessageType.Reliable, sessionUpdate);
                }

                if (Connection.State==ConnectionState.Disconnected)
                {
                    State = TrackerConnectionState.LostConnection;
                }
            }
        }

        private void HandleNewConnection()
        {
            NetworkMessage message;

            while (Connection.IncomingMessages.TryDequeue(out message))
            {
                if (message.Content is JoinSession)
                {
                    var request = (JoinSession)message.Content;
                    if (request.SessionName.Equals(SessionTracker.SessionName))
                    {
                        _joinSessionResponse =
                            Connection.EnqueueMessage(MessageType.Reliable, new JoinSessionResponse() { Accepted = true });
                        State = TrackerConnectionState.SentJoinResponse;
                        Console.WriteLine($"Connection {Connection.PeerPublicKey} requested to join session {SessionTracker.SessionName}");
                    }
                    else //client probably wants to connect to another tracker session
                    {
                        SessionTracker.RemoveConnection(this);
                        return;
                    }
                }
            }

            return;
        }
    }
}