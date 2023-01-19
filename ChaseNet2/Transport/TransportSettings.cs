namespace ChaseNet2.Transport
{
    public class TransportSettings
    {
        public int ReceiveBufferSize { get; set; } = 1024 * 1024 * 4;
        public int MaxMessageLength { get; set; } = 1024 * 1024 * 4;
        public int MaxBytesSentPerUpdate { get; set; } = 1024 * 1024 * 2;
        
        /// <summary>
        /// Simulated packet loss from 0 to 1
        /// </summary>
        public float SimulatedPacketLoss { get; set; } = 0;
    }
}