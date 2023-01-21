namespace ChaseNet2.Transport
{
    public class TransportSettings
    {
        /// <summary>
        /// UDP socket buffer size, required for large packets
        /// </summary>
        public int ReceiveBufferSize { get; set; } = 1024 * 1024 * 4;
        
        /// <summary>
        /// Maximum number of bytes to send in a single message.
        /// </summary>
        public int MaxMessageLength { get; set; } = 1024 * 1024 * 4;
        
        /// <summary>
        /// Throttles the number of messages sent per network tick.
        /// </summary>
        public int MaxBytesSentPerUpdate { get; set; } = 1024 * 1024 * 2;
        
        /// <summary>
        /// Max size of a UDP packet sent over the network.
        /// </summary>
        public int MaximumTransmissionUnit { get; set; } = 1500;
        
        /// <summary>
        /// Simulated packet loss from 0 to 1
        /// </summary>
        public float SimulatedPacketLoss { get; set; } = 0;
    }
}