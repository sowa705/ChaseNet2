using System.Net;
using ProtoBuf;

namespace ChaseNet2.Relay
{
    [ProtoContract]
    public class RelayRequest
    {
        [ProtoMember(1)]
        public IPEndPoint TargetEndPoint;
        [ProtoMember(2)]
        public ulong TargetConnectionID;
    }
}