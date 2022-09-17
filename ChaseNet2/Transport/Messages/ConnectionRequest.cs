using System.IO;
using System.Security.Cryptography;
using ChaseNet2.Serialization;

namespace ChaseNet2.Transport.Messages
{
    public class ConnectionRequest : IStreamSerializable
    {
        public ECDiffieHellmanPublicKey PublicKey { get; set; }
        public ulong ConnectionId { get; set; }
        public int Serialize(BinaryWriter writer)
        {
            writer.Write(ConnectionId);
            var pkBytes = PublicKey.ToByteArray()!;
            writer.Write(pkBytes.Length);
            writer.Write(pkBytes);
            return 8+4+pkBytes.Length;
        }
        public void Deserialize(BinaryReader reader)
        {
            ConnectionId = reader.ReadUInt64();
            var pkLength = reader.ReadInt32();
            var pkBytes = reader.ReadBytes(pkLength);
            PublicKey = ECDiffieHellmanCngPublicKey.FromByteArray(pkBytes, CngKeyBlobFormat.EccPublicBlob);
        }
    }
}