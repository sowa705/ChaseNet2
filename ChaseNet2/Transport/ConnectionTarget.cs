using System.IO;
using System.Net;
using System.Security.Cryptography;
using ChaseNet2.Serialization;
using Org.BouncyCastle.Crypto;

namespace ChaseNet2.Transport
{
    public class ConnectionTarget : IStreamSerializable
    {
        public AsymmetricKeyParameter PublicKey
        {
            get
            {
                if (_publicKey == null)
                {
                    if (_publicKeyBytes.Length<=1)
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
                if (_publicKey!=null)
                {
                    _publicKeyBytes = CryptoHelper.SerializePublicKey(_publicKey);
                }
                else
                {
                    _publicKeyBytes = new []{ (byte)0 };
                }
            }
        }

        public ulong ConnectionId { get; set; }
        public IPEndPoint EndPoint { get; set; }
        
        private byte[]? _publicKeyBytes;
        private AsymmetricKeyParameter? _publicKey;
        
        public int Serialize(BinaryWriter writer)
        {
            writer.Write(ConnectionId);
            var pkbytes = _publicKeyBytes;
            writer.Write(pkbytes.Length);
            writer.Write(pkbytes);
            
            var address = EndPoint.Address.GetAddressBytes();
            
            writer.Write(address.Length);
            writer.Write(address);
            writer.Write(EndPoint.Port);
            
            return 8 + 4 + pkbytes.Length + 4 + address.Length + 4;
        }

        public void Deserialize(BinaryReader reader)
        {
            ConnectionId = reader.ReadUInt64();
            var pklen = reader.ReadInt32();
            var pkbytes = reader.ReadBytes(pklen);
            _publicKeyBytes = pkbytes;

            var addrlen = reader.ReadInt32();
            var addrbytes = reader.ReadBytes(addrlen);
            var address = new IPAddress(addrbytes);
            
            var port = reader.ReadInt32();
            EndPoint = new IPEndPoint(address, port);
        }
    }
}