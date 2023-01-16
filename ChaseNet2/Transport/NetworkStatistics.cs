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
        public float AveragePing { get; set; }
        public float BitsSentPerSecond { get; set; }
        public float BitsReceivedPerSecond { get; set; }

        public override string ToString()
        {
            return string.Format("Packets Sent: {0}, Packets Received: {1}, Bytes Sent: {2}, Bytes Received: {3}, Average Update Time: {4} ms, Connection Count: {5}, Average Ping: {6} ms, kilobits Sent Per Second: {7} kb/s, kilobits Received Per Second: {8} kb/s",
                PacketsSent, PacketsReceived, BytesSent, BytesReceived, AverageUpdateTime.TotalMilliseconds.ToString("00.00"), ConnectionCount, AveragePing.ToString("00.00"), (BitsSentPerSecond/1024).ToString("000.0"), (BitsReceivedPerSecond/1024).ToString("000.0"));
        }
    }
}