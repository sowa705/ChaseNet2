using System;

namespace ChaseNet2.Transport
{
    [Flags]
    public enum MessageType
    {
        /// <summary>
        /// Network transport wont try to resend this message
        /// </summary>
        Unreliable = 0,
        /// <summary>
        /// Network transport will resend the message until acknowledged by the recipient.
        /// </summary>
        Reliable = 1,
        /// <summary>
        /// Will be prioritized when sending messages.
        /// </summary>
        Priority = 2,
        /// <summary>
        /// Bulk transfer insensitive to latency.
        /// </summary>
        Bulk = 4,
    }
}