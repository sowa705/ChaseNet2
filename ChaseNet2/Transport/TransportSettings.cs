namespace ChaseNet2.Transport
{
    public class TransportSettings
    {
        /// <summary>
        /// UDP socket buffer size, required for large packets.
        /// Linux silently clamps this to net.core.rmem_max (212992 bytes by default), so
        /// <see cref="MaxBytesSentPerUpdate"/> must stay small enough for a burst to fit in the smallest buffer.
        /// </summary>
        public int ReceiveBufferSize { get; set; } = 1024 * 1024 * 32;

        /// <summary>D
        /// Maximum number of bytes to send in a single message.
        /// </summary>
        public int MaxMessageLength { get; set; } = 1024 * 1024 * 16;

        /// <summary>
        /// Throttles the number of bytes sent per connection per network tick. Sending more than the peer's socket
        /// buffer holds in one burst makes the OS drop the excess, which for large messages causes long resend storms.
        /// The default fits the default Linux buffer; raise it together with net.core.rmem_max.
        /// </summary>
        public int MaxBytesSentPerUpdate { get; set; } = 64 * 1024;

        /// <summary>
        /// Max size of a UDP packet sent over the network.
        /// </summary>
        public int MaximumTransmissionUnit { get; set; } = 1300;

        /// <summary>
        /// Simulated packet loss from 0 to 1
        /// </summary>
        public float SimulatedPacketLoss { get; set; } = 0;
        
        /// <summary>
        /// 
        /// </summary>
        public bool AcceptNewConnections { get; set; } = false;
        public int MaximumConnections { get; set; } = 16; // default is maximum limit, can be changed according to your needs.
        
        /// <summary>
        /// The rate at which background thread will update connections in updates per second.
        /// Recommended value on tracker servers is 20, clients can use 60-120.
        /// </summary>
        public float TargetUpdateRate { get; set; } = 30;
    }
}