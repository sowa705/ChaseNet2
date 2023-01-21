using System.IO;
using System.Net;
using System.Security.Cryptography;
using ChaseNet2.Serialization;
using Org.BouncyCastle.Crypto;
using ProtoBuf;

namespace ChaseNet2.Transport
{
    [ProtoContract]
    public class ConnectionTarget
    {
        public AsymmetricKeyParameter? PublicKey
        {
            get
            {
                if (_publicKey == null)
                {
                    if (_publicKeyBytes.Length <= 1)
                    {
                        return null;
                    }

                    _publicKey = CryptoHelper.DeserializePublicKey(_publicKeyBytes);
                }
                return _publicKey;
            }
            set
            {
                _publicKey = value;
                if (_publicKey != null)
                {
                    _publicKeyBytes = CryptoHelper.SerializePublicKey(_publicKey);
                }
                else
                {
                    _publicKeyBytes = new[] { (byte)0 };
                }
            }
        }

        [ProtoMember(1)]
        public ulong ConnectionId { get; set; }
        [ProtoMember(2)]
        public IPEndPoint EndPoint { get; set; }

        [ProtoMember(3)]
        private byte[]? _publicKeyBytes;

        private AsymmetricKeyParameter? _publicKey;
    }
}