using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChaseNet2.Transport;
using ChaseNet2.Transport.Messages;
using Serilog;

namespace ChaseNet2.Session
{
    public class SessionTracker: ConnectionHandler
    {
        public ConnectionManager ConnectionManager { get; set; }
        public string SessionName { get; set; }
        
        public List<TrackerConnection> Connections { get; set; }
        
        public SessionTracker(ConnectionManager connectionManager)
        {
            Connections = new List<TrackerConnection>();
            ConnectionManager = connectionManager;
            
            ConnectionManager.AttachHandler(this);
            connectionManager.AcceptNewConnections = true;
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
            Connections.RemoveAll(x=>x.Connection.State == ConnectionState.Disconnected);
        }
    }
}