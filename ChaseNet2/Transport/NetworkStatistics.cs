using System;

namespace ChaseNet2.Transport
{
    public class NetworkStatistics
    {
        public int PacketsSent { get; set; }
        public int PacketsReceived { get; set; }
        public int BytesSent { get; set; }
        public int BytesReceived { get; set; }
        public TimeSpan AverageUpdateTime { get; set; }

        public int ConnectionCount { get; set; }

        public override string ToString()
        {
            return
                $"Packets sent: {PacketsSent}, Packets received: {PacketsReceived}, Bytes sent: {BytesSent}, Bytes received: {BytesReceived}, Average update time: {AverageUpdateTime.TotalMilliseconds.ToString("00.00")} ms, Connections: {ConnectionCount}";
        }
    }
}