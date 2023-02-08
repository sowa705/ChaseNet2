using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace ChaseNet2.Transport
{
    public abstract class ConnectionHandler
    {
        public List<ulong> ConnectionIDs = new List<ulong>();

        /// <summary>
        /// Called when the connection handler gets attached to the manager
        /// </summary>
        /// <param name="manager"></param>
        /// <returns></returns>
        public abstract Task OnHandlerAttached(ConnectionManager manager);

        /// <summary>
        /// Called when ConnectionManager gets a new incoming connection on all handlers
        /// </summary>
        public abstract Task OnConnectionAttached(Connection connection);

        /// <summary>
        /// Called after connection has updated
        /// </summary>
        /// <param name="connection"></param>
        public abstract void OnConnectionUpdated(Connection connection);

        /// <summary>
        /// Called on manager update
        /// </summary>
        public abstract void OnManagerUpdated();

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