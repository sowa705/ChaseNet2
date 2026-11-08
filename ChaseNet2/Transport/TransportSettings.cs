using System;

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

        /// <summary>
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
        /// Whether incoming connection requests are accepted.
        /// </summary>
        public bool AcceptNewConnections { get; set; } = false;
        public int MaximumConnections { get; set; } = 16; // default is maximum limit, can be changed according to your needs.

        /// <summary>
        /// The rate at which the network thread (<see cref="ConnectionManager.Start"/>) updates connections, in updates per second.
        /// Incoming packets are read as soon as they arrive; this rate paces sends, resends and pings.
        /// Recommended value on tracker servers is 20, clients can use 60-120.
        /// </summary>
        public float TargetUpdateRate { get; set; } = 30;

        /// <summary>
        /// How often a ping is sent on each connection.
        /// </summary>
        public TimeSpan PingInterval { get; set; } = TimeSpan.FromSeconds(2);

        /// <summary>
        /// A connection with no pong for this long is marked <see cref="ConnectionState.Disconnected"/>.
        /// </summary>
        public TimeSpan PongTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Upper bound on messages and events waiting for <see cref="ConnectionManager.Dispatch"/>.
        /// When it is reached, new unreliable messages are dropped (counted in <see cref="NetworkStatistics.DroppedReceivedMessages"/>).
        /// Reliable messages and connection events are never dropped, and sequenced messages never queue more than
        /// one message per connection and channel, because a newer one replaces the queued one.
        /// </summary>
        public int MaxPendingDispatchItems { get; set; } = 8192;
    }
}
