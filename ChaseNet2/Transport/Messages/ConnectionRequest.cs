using System.IO;
using ChaseNet2.Serialization;
using Org.BouncyCastle.Crypto;
using ProtoBuf;

namespace ChaseNet2.Transport.Messages
{
    [ProtoContract]
    public class ConnectionRequest
    {
        [ProtoMember(1)]
        public AsymmetricKeyParameter PublicKey { get; set; }
        [ProtoMember(2)]
        public ulong ConnectionId { get; set; }
    }
}