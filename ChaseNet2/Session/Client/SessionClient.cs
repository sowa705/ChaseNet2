using System;
using System.Linq;
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
        private ConnectionManager _connectionManager;
        public SessionClientState State { get; private set; }
        public SessionUpdate LastSessionUpdate { get; private set; }

        public ulong TrackerConnectionId { get => _trackerConnection.ConnectionId; }

        NetworkMessage _connectMessage;
        public string SessionId { get; private set; }
        public SessionClient(string sessionName, ConnectionManager manager, Connection trackerConnection)
        {
            _connectionManager = manager;
            _connectionManager.AttachHandler(this);
            AddConnection(trackerConnection.ConnectionId);
            _trackerConnection = trackerConnection;
            SessionId = sessionName;

            _trackerConnection.RegisterMessageHandler((ulong)InternalChannelType.TrackerInternal, new SessionClientMessageHandler(this));
        }

        public override Task OnAttached(ConnectionManager manager)
        {
            return Task.CompletedTask;
        }

        public override Task OnManagerConnect(Connection connection) // client does not accept incoming connections so we don't need to do anything here
        {
            return Task.CompletedTask;
        }

        public override void ConnectionUpdate(Connection connection)
        {

        }

        public override void Update()
        {
        }

        public async Task Connect()
        {
            var joinSessionMessage = new JoinSession() { SessionName = SessionId };

            _connectMessage = _trackerConnection.EnqueueMessage(MessageType.Reliable, (ulong)InternalChannelType.SessionJoin, joinSessionMessage);
            await _trackerConnection.WaitForDeliveryAsync(_connectMessage);

            if (_connectMessage.State == MessageState.Failed)
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

        public void ProcessSessionUpdate(SessionUpdate update)
        {
            // update our session state
            LastSessionUpdate = update;

            // update our connections to match the session state
            var ConnectionsToAdd = update.Peers.Where(x =>
                _connectionManager.Connections.FirstOrDefault(y => y.ConnectionId == x.ConnectionId) == null);

            var ConnectionsToRemove = ConnectionIDs.Where(x =>
                update.Peers.FirstOrDefault(y => y.ConnectionId == x) == null).ToList();

            foreach (var connection in ConnectionsToAdd)
            {
                if (connection.ConnectionId == 0) // this is us
                    continue;
                Log.Logger.Information("Connecting to a new peer with connectionID {ConnectionId}", connection.ConnectionId, SessionId);
                _connectionManager.AttachConnectionAsync(connection).Wait();
                AddConnection(connection.ConnectionId);
            }
            foreach (var connection in ConnectionsToRemove)
            {
                if (connection == 0 || connection == _trackerConnection.ConnectionId) // this is us
                    continue;
                _connectionManager.RemoveConnection(connection);
                RemoveConnection(connection);
            }
        }
    }

    public class SessionClientMessageHandler : IMessageHandler
    {
        SessionClient _client;
        public SessionClientMessageHandler(SessionClient client)
        {
            _client = client;
        }

        public void HandleMessage(Connection connection, NetworkMessage message)
        {
            switch (message.Content)
            {
                case SessionUpdate update:
                    _client.ProcessSessionUpdate(update);
                    break;
            }
        }
    }
}