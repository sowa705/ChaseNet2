using System.IO;
using System.Security.Cryptography;
using ChaseNet2.Serialization;

namespace ChaseNet2.Transport.Messages
{
    public class ConnectionRequest : IStreamSerializable
    {
        public ECDiffieHellmanPublicKey PublicKey { get; set; }
        
        public void Serialize(object obj, BinaryWriter writer)
        {
            var pkBytes = PublicKey.ToByteArray()!;
            writer.Write(pkBytes.Length);
            writer.Write(pkBytes);
        }
        public void Deserialize(BinaryReader reader)
        {
            var pkLength = reader.ReadInt32();
            var pkBytes = reader.ReadBytes(pkLength);
            PublicKey = ECDiffieHellmanCngPublicKey.FromByteArray(pkBytes, CngKeyBlobFormat.EccPublicBlob);
        }
    }
}