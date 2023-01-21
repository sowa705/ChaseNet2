using System.Net;
using ProtoBuf;

namespace ChaseNet2.Transport.Messages
{
    [ProtoContract]
    public class IPEndPointSurrogate
    {
        [ProtoMember(1)]
        public byte[] addressBytes;
        [ProtoMember(2)]
        public int port;

        public static implicit operator IPEndPoint(IPEndPointSurrogate surrogate)
        {
            if (surrogate == null)
                return null;
            return new IPEndPoint(new IPAddress(surrogate.addressBytes), surrogate.port);
        }

        public static implicit operator IPEndPointSurrogate(IPEndPoint endpoint)
        {
            if (endpoint == null)
                return null;
            return new IPEndPointSurrogate { addressBytes = endpoint.Address.GetAddressBytes(), port = endpoint.Port };
        }
    }
}