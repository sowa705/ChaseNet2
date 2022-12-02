using System.IO;
using ChaseNet2.Serialization;
using Org.BouncyCastle.Crypto;

namespace ChaseNet2.Transport.Messages
{
    public class ConnectionRequest : IStreamSerializable
    {
        public AsymmetricKeyParameter PublicKey { get; set; }
        public ulong ConnectionId { get; set; }
        public int Serialize(BinaryWriter writer)
        {
            writer.Write(ConnectionId);
            var pkBytes = CryptoHelper.SerializePublicKey(PublicKey);
            writer.Write(pkBytes.Length);
            writer.Write(pkBytes);
            return 8+4+pkBytes.Length;
        }
        public void Deserialize(BinaryReader reader)
        {
            ConnectionId = reader.ReadUInt64();
            var pkLength = reader.ReadInt32();
            var pkBytes = reader.ReadBytes(pkLength);
            PublicKey = CryptoHelper.DeserializePublicKey(pkBytes);
        }
    }
}