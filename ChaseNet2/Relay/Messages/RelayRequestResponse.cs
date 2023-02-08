using ProtoBuf;

namespace ChaseNet2.Relay
{
    [ProtoContract]
    public class RelayRequestResponse
    {
        [ProtoMember(1)]
        public bool Accepted;
    }
}