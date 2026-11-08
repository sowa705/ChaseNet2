using System;
using System.Threading;

namespace ChaseNet2.Transport
{
    /// <summary>
    /// Transport counters. Written by the network thread, safe to read from any thread.
    /// </summary>
    public class NetworkStatistics
    {
        private long _packetsSent;
        private long _packetsReceived;
        private long _bytesSent;
        private long _bytesReceived;
        private long _droppedReceivedMessages;
        private long _averageUpdateTimeTicks;
        private int _connectionCount;
        private float _averagePing;
        private float _bitsSentPerSecond;
        private float _bitsReceivedPerSecond;

        public long PacketsSent => Interlocked.Read(ref _packetsSent);
        public long PacketsReceived => Interlocked.Read(ref _packetsReceived);
        public long BytesSent => Interlocked.Read(ref _bytesSent);
        public long BytesReceived => Interlocked.Read(ref _bytesReceived);

        /// <summary>
        /// Unreliable messages discarded because the queue for the dispatching thread was full.
        /// </summary>
        public long DroppedReceivedMessages => Interlocked.Read(ref _droppedReceivedMessages);

        /// <summary>
        /// Smoothed duration of one network pump (receive, resend, send) - not including handler dispatch.
        /// </summary>
        public TimeSpan AverageUpdateTime
        {
            get => TimeSpan.FromTicks(Interlocked.Read(ref _averageUpdateTimeTicks));
            internal set => Interlocked.Exchange(ref _averageUpdateTimeTicks, value.Ticks);
        }

        public int ConnectionCount
        {
            get => Volatile.Read(ref _connectionCount);
            internal set => Volatile.Write(ref _connectionCount, value);
        }

        public float AveragePing
        {
            get => Volatile.Read(ref _averagePing);
            internal set => Volatile.Write(ref _averagePing, value);
        }

        public float BitsSentPerSecond
        {
            get => Volatile.Read(ref _bitsSentPerSecond);
            internal set => Volatile.Write(ref _bitsSentPerSecond, value);
        }

        public float BitsReceivedPerSecond
        {
            get => Volatile.Read(ref _bitsReceivedPerSecond);
            internal set => Volatile.Write(ref _bitsReceivedPerSecond, value);
        }

        internal void AddSentPacket(int bytes)
        {
            Interlocked.Increment(ref _packetsSent);
            Interlocked.Add(ref _bytesSent, bytes);
        }

        internal void AddReceivedPacket(int bytes)
        {
            Interlocked.Increment(ref _packetsReceived);
            Interlocked.Add(ref _bytesReceived, bytes);
        }

        internal void AddDroppedReceivedMessage()
        {
            Interlocked.Increment(ref _droppedReceivedMessages);
        }

        public override string ToString()
        {
            return string.Format("Packets Sent: {0}, Packets Received: {1}, Bytes Sent: {2}, Bytes Received: {3}, Average Update Time: {4} ms, Connection Count: {5}, Average Ping: {6} ms, kilobits Sent Per Second: {7} kb/s, kilobits Received Per Second: {8} kb/s",
                PacketsSent, PacketsReceived, BytesSent, BytesReceived, AverageUpdateTime.TotalMilliseconds.ToString("00.00"), ConnectionCount, AveragePing.ToString("00.00"), (BitsSentPerSecond / 1024).ToString("000.0"), (BitsReceivedPerSecond / 1024).ToString("000.0"));
        }
    }
}
