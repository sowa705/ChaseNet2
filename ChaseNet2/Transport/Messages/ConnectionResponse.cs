using System;
using System.IO;
using System.Security.Cryptography;
using ChaseNet2.Serialization;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Security;
using ProtoBuf;

namespace ChaseNet2.Transport.Messages
{
    [ProtoContract]
    public class ConnectionResponse
    {
        [ProtoMember(1)]
        public bool Accepted { get; set; }
        [ProtoMember(2)]
        public ulong ConnectionId { get; set; }
        [ProtoMember(3)]
        public AsymmetricKeyParameter PublicKey { get; set; }
    }
}