using System;
using System.Collections.Generic;

namespace ChaseNet2.Transport
{
    /// <summary>
    /// Tracking record for a reliable message awaiting acknowledgement. Owned by the network thread.
    /// </summary>
    public class SentMessage
    {
        public NetworkMessage Message { get; set; }
        public DateTime LastSent { get; set; }
        public int ResendCount { get; set; }
        public bool IsSplit { get; set; }
        public List<NetworkMessage> SentFragmentMessages { get; set; }

        /// <summary>
        /// For a fragment: the tracking record of the split message it belongs to.
        /// </summary>
        public SentMessage? SplitParent { get; set; }

        /// <summary>
        /// For a split message: when a fragment was last acknowledged (or when it was split).
        /// </summary>
        public DateTime LastProgress { get; set; }

        /// <summary>
        /// For a split message: number of fragments acknowledged so far.
        /// </summary>
        public int DeliveredFragmentCount { get; set; }

        public SentMessage(NetworkMessage message)
        {
            Message = message;
            ResendCount = 0;
            LastSent = DateTime.UtcNow;
            SentFragmentMessages = new List<NetworkMessage>();
        }
    }
}
