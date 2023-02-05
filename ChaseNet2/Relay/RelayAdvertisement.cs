using ProtoBuf;

namespace ChaseNet2.Relay
{
    [ProtoContract]
    public class RelayAdvertisement
    {
        public ushort PreferredMTU;
    }
}