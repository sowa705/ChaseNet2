namespace ChaseNet2.Transport
{
    public class TransportSettings
    {
        public int ReceiveBufferSize { get; set; } = 1024 * 1024 * 4;
        public int MaxMessageLength { get; set; } = 1024 * 1024 * 4;
        public int MaxBytesSentPerUpdate { get; set; } = 1024 * 1024 * 2;
    }
}