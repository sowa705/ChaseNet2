using System.IO;
using ChaseNet2.Serialization;
using ProtoBuf;

namespace ChaseNet2.Transport.Messages
{
    [ProtoContract]
    public class Ping
    {
        [ProtoMember(1)]
        public int RandomNumber { get; set; }
    }
}