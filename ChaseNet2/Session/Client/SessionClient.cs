using System;
using ChaseNet2.Session.Messages;
using ChaseNet2.Transport;

namespace ChaseNet2.Session
{
    public class SessionClient
    {
        public Connection TrackerConnection { get; private set; }
        
        public SessionClientState State { get; private set; }
        
        NetworkMessage _connectMessage;
        public string SessionId { get; private set; }
        public SessionClient(string sessionName,Connection trackerConnection)
        {
            TrackerConnection = trackerConnection;
            SessionId = sessionName;
            
            SendConnectMessage();
        }
        
        void SendConnectMessage()
        {            
            State = SessionClientState.StartedConnection;
            _connectMessage=TrackerConnection.EnqueueMessage(MessageType.Reliable, new JoinSession() { SessionName = SessionId });
        }

        public void Update()
        {
            if (State==SessionClientState.StartedConnection)
            {
                if (_connectMessage.State==MessageState.Delivered)
                {
                    State = SessionClientState.AwaitingResponse;
                    Console.WriteLine($"Connection message delivered");
                }

                if (_connectMessage.State==MessageState.Failed) //retry
                {
                    SendConnectMessage();
                    Console.WriteLine($"Failed to send connection message, retrying");
                }
            }

            if (State==SessionClientState.AwaitingResponse)
            {
                NetworkMessage message;
                while (TrackerConnection.IncomingMessages.TryDequeue(out message))
                {
                    if (message.Content is JoinSessionResponse)
                    {
                        var response = (JoinSessionResponse) message.Content;
                        if (response.Accepted)
                        {
                            State = SessionClientState.Connected;
                            Console.WriteLine($"Connected to session {SessionId}");
                        }
                        else
                        {
                            State = SessionClientState.Rejected;
                            Console.WriteLine($"Rejected from session {SessionId}");
                        }
                    }
                }
            }

            if (State == SessionClientState.Connected)
            {
                NetworkMessage message;
                while (TrackerConnection.IncomingMessages.TryDequeue(out message))
                {
                    if (message.Content is SessionUpdate)
                    {
                        var update = (SessionUpdate) message.Content;
                        Console.WriteLine($"Received update from session {SessionId}");
                    }
                }
            }
        }
    }
}