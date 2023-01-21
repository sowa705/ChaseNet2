using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using ChaseNet2.Extensions;
using ChaseNet2.Serialization;
using ChaseNet2.Transport;
using ProtoBuf;

namespace ChaseNet2.Session.Messages
{
    [ProtoContract]
    public class SessionUpdate
    {
        [ProtoMember(1)]
        public List<ConnectionTarget> Peers { get; set; }

        public SessionUpdate()
        {
            Peers = new List<ConnectionTarget>();
        }
    }
}