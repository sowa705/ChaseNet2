using System;
using System.Collections.Generic;
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
        
        public void RemoveConnection(TrackerConnection connection)
        {
            Connections.Remove(connection);
        }

        public override void OnManagerConnect(Connection connection)
        {
            Log.Logger.Information("New connection established");
            var c = new TrackerConnection() { Connection = connection, SessionTracker = this };
            Connections.Add(c);
            AddConnection(connection.ConnectionId);
            Log.Logger.Information("Connection added to list");
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
    }
}