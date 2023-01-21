using System.IO;
using ChaseNet2.Serialization;
using ProtoBuf;

namespace ChaseNet2.Transport.Messages
{
    [ProtoContract]
    public class Ack
    {
        [ProtoMember(1)]
        public ulong MessageID { get; set; }
    }
}