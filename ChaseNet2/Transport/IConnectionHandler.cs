using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace ChaseNet2.Transport
{
    public abstract class ConnectionHandler
    {
        public List<ulong> ConnectionIDs = new List<ulong>();

        public abstract Task OnAttached(ConnectionManager manager);

        /// <summary>
        /// Called when ConnectionManager gets a new incoming connection on all handlers
        /// </summary>
        public abstract Task OnManagerConnect(Connection connection);

        public abstract void ConnectionUpdate(Connection connection);

        public abstract void Update();

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