using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChaseNet2.Transport;
using ChaseNet2.Transport.Messages;
using Serilog;

namespace ChaseNet2.Session
{
    public class SessionTracker : ConnectionHandler
    {
        public ConnectionManager ConnectionManager { get; set; }
        public string SessionName { get; set; }

        public List<TrackerConnection> Connections { get; set; }

        public SessionTracker()
        {
            Connections = new List<TrackerConnection>();
        }

        public override Task OnAttached(ConnectionManager manager)
        {
            ConnectionManager = manager;
            ConnectionManager.AcceptNewConnections = true;
            return Task.CompletedTask;
        }

        public override async Task OnManagerConnect(Connection connection)
        {
            var c = new TrackerConnection() { Connection = connection, SessionTracker = this };
            Connections.Add(c);
            AddConnection(connection.ConnectionId);
            c.HandleNewConnection();
        }

        public override void ConnectionUpdate(Connection connection)
        {
            var trackerConnection = Connections.Find(c => c.Connection == connection);

            if (trackerConnection != null)
            {
                trackerConnection.Update();
            }
        }

        public override void Update()
        {
            var connectionsToRemove = Connections.Where(x => x.Connection.State == ConnectionState.Disconnected);
            foreach (var connection in connectionsToRemove)
            {
                ConnectionManager.RemoveConnection(connection.Connection.ConnectionId);
            }
            Connections.RemoveAll(x => x.Connection.State == ConnectionState.Disconnected);
        }
    }
}