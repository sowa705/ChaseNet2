using System;
using System.Collections.Generic;
using ChaseNet2.Transport;
using ChaseNet2.Transport.Messages;

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
            
            connectionManager.AcceptNewConnections = true;
        }
        
        public void RemoveConnection(TrackerConnection connection)
        {
            Connections.Remove(connection);
        }

        public override void OnManagerConnect(Connection connection)
        {
            Console.WriteLine("New connection established");
            var c = new TrackerConnection() { Connection = connection, SessionTracker = this };
            Connections.Add(c);
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