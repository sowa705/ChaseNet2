using System.Collections.Generic;

namespace ChaseNet2.Transport
{
    public abstract class ConnectionHandler
    {
        List<ulong> ConnectionIDs = new List<ulong>();
        
        /// <summary>
        /// Called when ConnectionManager gets a new incoming connection on all handlers
        /// </summary>
        public abstract void OnManagerConnect(Connection connection);

        public abstract void ConnectionUpdate(Connection connection);
        
        public void AddConnection(ulong ConnectionID)
        {
            ConnectionIDs.Add(ConnectionID);
        }
        public void RemoveConnection(ulong ConnectionID)
        {
            ConnectionIDs.Remove(ConnectionID);
        }
        
        public bool ShouldHandle(ulong ConnectionID)
        {
            return ConnectionIDs.Contains(ConnectionID);
        }
    }
}