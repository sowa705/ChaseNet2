using System.IO;
using System.Net;
using System.Security.Cryptography;
using ChaseNet2.Serialization;

namespace ChaseNet2.Transport
{
    public class ConnectionTarget : IStreamSerializable
    {
        public ECDiffieHellmanPublicKey PublicKey
        {
            //this is an optimization because instantiating public key class is extremely slow (calls to internal windows bullshit) and we don't need to do it most of the time
            get
            {
                if (_publicKey == null)
                {
                    if (_publicKeyBytes.Length<=1)
                    {
                        return null;
                    }
                    _publicKey = ECDiffieHellmanCngPublicKey.FromByteArray(_publicKeyBytes, CngKeyBlobFormat.EccPublicBlob);
                }
                return _publicKey;
            }
            set
            {
                _publicKey = value;
                if (_publicKey!=null)
                {
                    _publicKeyBytes = _publicKey.ToByteArray();
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
        private ECDiffieHellmanPublicKey? _publicKey;
        
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