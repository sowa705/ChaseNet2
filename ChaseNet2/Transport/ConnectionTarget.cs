using System.IO;
using System.Net;
using System.Security.Cryptography;
using ChaseNet2.Serialization;

namespace ChaseNet2.Transport
{
    public class ConnectionTarget : IStreamSerializable
    {
        public ECDiffieHellmanPublicKey PublicKey { get; set; }
        public ulong ConnectionID { get; set; }
        public IPEndPoint EndPoint { get; set; }
        public int Serialize(BinaryWriter writer)
        {
            writer.Write(ConnectionID);
            var pkbytes = PublicKey.ToByteArray();
            writer.Write(pkbytes.Length);
            writer.Write(pkbytes);
            
            var address = EndPoint.Address.GetAddressBytes();
            
            writer.Write(address.Length);
            writer.Write(address);
            writer.Write(EndPoint.Port);
            
            return 4 + pkbytes.Length + 4 + address.Length + 4;
        }

        public void Deserialize(BinaryReader reader)
        {
            ConnectionID = reader.ReadUInt64();
            var pklen = reader.ReadInt32();
            var pkbytes = reader.ReadBytes(pklen);
            //PublicKey = ECDiffieHellmanCngPublicKey.FromByteArray(pkbytes, CngKeyBlobFormat.EccPublicBlob);
            
            var addrlen = reader.ReadInt32();
            var addrbytes = reader.ReadBytes(addrlen);
            var address = new IPAddress(addrbytes);
            
            var port = reader.ReadInt32();
            EndPoint = new IPEndPoint(address, port);
        }
    }
}