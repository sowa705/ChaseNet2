using System.IO;
using System.Security.Cryptography;
using ChaseNet2.Serialization;

namespace ChaseNet2.Transport.Messages
{
    public class ConnectionResponse : IStreamSerializable
    {
        public bool Accepted { get; set; }
        public ulong ConnectionId { get; set; }
        public ECDiffieHellmanPublicKey PublicKey { get; set; }
        public int Serialize(BinaryWriter writer)
        {
            writer.Write(Accepted);
            writer.Write(ConnectionId);
            var pkBytes = PublicKey.ToByteArray()!;
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
            PublicKey = ECDiffieHellmanCngPublicKey.FromByteArray(pkBytes, CngKeyBlobFormat.EccPublicBlob);
        }
    }
}