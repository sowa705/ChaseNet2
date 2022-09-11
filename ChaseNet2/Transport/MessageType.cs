using System;

namespace ChaseNet2
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
        /// This is a service message used internally by network transport.
        /// </summary>
        Internal = 2,
    }
}