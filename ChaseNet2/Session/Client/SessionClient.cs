using System;
using System.Threading.Tasks;
using ChaseNet2.Session.Messages;
using ChaseNet2.Transport;

namespace ChaseNet2.Session
{
    public class SessionClient : ConnectionHandler
    {
        private Connection _trackerConnection;
        public SessionClientState State { get; private set; }
        
        NetworkMessage _connectMessage;
        public string SessionId { get; private set; }
        public SessionClient(string sessionName, Connection trackerConnection)
        {
            AddConnection(trackerConnection.ConnectionId);
            _trackerConnection = trackerConnection;
            SessionId = sessionName;
        }
        public override void OnManagerConnect(Connection connection) // client does not accept incoming connections
        {
        }

        public override void ConnectionUpdate(Connection connection)
        {
            
        }

        public async Task Connect()
        {
            var joinSessionMessage = new JoinSession() { SessionName = SessionId };
            
            _connectMessage = _trackerConnection.EnqueueMessage(MessageType.Reliable, (ulong) InternalChannelType.SessionJoin, joinSessionMessage);
            await _trackerConnection.WaitForDeliveryAsync(_connectMessage);

            if (_connectMessage.State==MessageState.Failed)
            {
                throw new Exception("Failed to connect to session");
            }
            State = SessionClientState.AwaitingResponse;

            var message = await _trackerConnection.WaitForChannelMessageAsync((ulong)InternalChannelType.SessionJoin,
                TimeSpan.FromSeconds(3));

            var response = message.Content as JoinSessionResponse;

            if (response.Accepted)
            {
                State = SessionClientState.Connected;
            }
            else
            {
                State = SessionClientState.Disconnected;
            }
        }
    }
}