using System;
using System.Threading.Tasks;
using ChaseNet2.Session.Messages;
using ChaseNet2.Transport;
using Serilog;
using Serilog.Core;

namespace ChaseNet2.Session
{
    public class SessionClient : ConnectionHandler
    {
        private Connection _trackerConnection;
        public SessionClientState State { get; private set; }
        
        NetworkMessage _connectMessage;
        public string SessionId { get; private set; }
        public SessionClient(string sessionName, ConnectionManager manager, Connection trackerConnection)
        {
            manager.AttachHandler(this);
            AddConnection(trackerConnection.ConnectionId);
            _trackerConnection = trackerConnection;
            SessionId = sessionName;
            
            _trackerConnection.RegisterMessageHandler((ulong) InternalChannelType.TrackerInternal, new SessionClientMessageHandler());
        }
        public override void OnManagerConnect(Connection connection) // client does not accept incoming connections
        {
            Log.Logger.Warning("SessionClient.OnManagerConnect() called on client");
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
    
    public class SessionClientMessageHandler : IMessageHandler
    {
        public void HandleMessage(Connection connection, NetworkMessage message)
        {
            Log.Logger.Information("Received message from tracker");
            switch (message.Content)
            {
                case SessionUpdate update:
                    Log.Logger.Error("Received session update");
                    break;
            }
        }
    }
}