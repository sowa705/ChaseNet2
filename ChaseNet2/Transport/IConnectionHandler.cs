using System.Collections.Generic;
using System.Threading.Tasks;

namespace ChaseNet2.Transport
{
    /// <summary>
    /// Application-level logic attached to a <see cref="ConnectionManager"/>.
    /// Every callback except <see cref="OnAttached"/> runs on the dispatching thread
    /// (the thread calling <see cref="ConnectionManager.Dispatch"/> or <see cref="ConnectionManager.Update"/>).
    /// </summary>
    public abstract class ConnectionHandler
    {
        private readonly object _connectionIdsLock = new object();

        public List<ulong> ConnectionIDs = new List<ulong>();

        /// <summary>
        /// Called from <see cref="ConnectionManager.AttachHandler"/> on the calling thread.
        /// </summary>
        public abstract Task OnAttached(ConnectionManager manager);

        /// <summary>
        /// Called on all handlers when a connection is added to the manager (an accepted incoming connection or
        /// <see cref="ConnectionManager.AttachConnectionAsync"/>). Runs on the dispatching thread before any queued
        /// message from that connection is dispatched, so handlers registered here see the first message.
        /// </summary>
        public abstract Task OnManagerConnect(Connection connection);

        /// <summary>
        /// Called on all handlers after <see cref="ConnectionManager.RemoveConnection"/>, on the dispatching thread.
        /// </summary>
        public virtual void OnManagerDisconnect(Connection connection)
        {
        }

        /// <summary>
        /// Called once per dispatch for every connection this handler handles (see <see cref="AddConnection"/>).
        /// </summary>
        public abstract void ConnectionUpdate(Connection connection);

        /// <summary>
        /// Called once per dispatch.
        /// </summary>
        public abstract void Update();

        public void AddConnection(ulong ConnectionID)
        {
            lock (_connectionIdsLock)
            {
                ConnectionIDs.Add(ConnectionID);
            }
        }

        public void RemoveConnection(ulong ConnectionID)
        {
            lock (_connectionIdsLock)
            {
                ConnectionIDs.Remove(ConnectionID);
            }
        }

        public bool ShouldHandle(ulong ConnectionID)
        {
            lock (_connectionIdsLock)
            {
                return ConnectionIDs.Contains(ConnectionID);
            }
        }
    }
}
