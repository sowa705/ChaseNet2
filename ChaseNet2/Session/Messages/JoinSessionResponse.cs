using System.IO;
using ChaseNet2.Extensions;
using ChaseNet2.Serialization;
using ProtoBuf;

namespace ChaseNet2.Session.Messages
{
    [ProtoContract]
    public class JoinSessionResponse
    {
        [ProtoMember(1)]
        public bool Accepted { get; set; }
    }
}