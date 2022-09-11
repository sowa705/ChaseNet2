using System;
using System.Collections.Generic;
using ChaseNet2.Transport;
using ChaseNet2.Transport.Messages;

namespace ChaseNet2.Session
{
    public class SessionTracker
    {
        public ConnectionManager ConnectionManager { get; set; }
        public string SessionName { get; set; }
        
        public List<TrackerConnection> Connections { get; set; }
        
        public SessionTracker(ConnectionManager connectionManager)
        {
            Connections = new List<TrackerConnection>();
            ConnectionManager = connectionManager;
            
            connectionManager.AcceptNewConnections = true;
            connectionManager.OnConnectionEstablished += OnConnectionEstablished;
        }

        private void OnConnectionEstablished(object sender, Connection e)
        {
            Console.WriteLine("New connection established");
            Connections.Add(new TrackerConnection(){Connection = e, SessionTracker = this});
        }
        /// <summary>
        /// Updates tracker, call ConnectionManager.Update() before
        /// </summary>
        public void Update()
        {
            foreach (var connection in Connections)
            {
                connection.Update();
            }
        }
        
        public void RemoveConnection(TrackerConnection connection)
        {
            Connections.Remove(connection);
        }
    }
}