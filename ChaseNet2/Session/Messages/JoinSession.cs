using System.IO;
using ChaseNet2.Extensions;
using ChaseNet2.Serialization;
using ProtoBuf;

namespace ChaseNet2.Session.Messages
{
    [ProtoContract]
    public class JoinSession
    {
        [ProtoMember(1)]
        public string SessionName { get; set; }
    }
}