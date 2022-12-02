using System.IO;
using System.Security.Cryptography;
using ChaseNet2.Serialization;
using Org.BouncyCastle.Crypto;

namespace ChaseNet2.Transport.Messages
{
    public class ConnectionResponse : IStreamSerializable
    {
        public bool Accepted { get; set; }
        public ulong ConnectionId { get; set; }
        public AsymmetricKeyParameter PublicKey { get; set; }
        public int Serialize(BinaryWriter writer)
        {
            writer.Write(Accepted);
            writer.Write(ConnectionId);
            var pkBytes = CryptoHelper.SerializePublicKey(PublicKey);
            writer.Write(pkBytes.Length);
            writer.Write(pkBytes);
            return 1+8+4+pkBytes.Length;
        }
        public void Deserialize(BinaryReader reader)
        {
            Accepted = reader.ReadBoolean();
            ConnectionId = reader.ReadUInt64();
            var pkLength = reader.ReadInt32();
            var pkBytes = reader.ReadBytes(pkLength);
            PublicKey = CryptoHelper.DeserializePublicKey(pkBytes);
        }
    }
}