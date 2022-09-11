using System.IO;
using ChaseNet2.Serialization;

namespace ChaseNet2.Transport.Messages
{
    public class Ack:IStreamSerializable
    {
        public long MessageID { get; set; }
        public int Serialize(BinaryWriter writer)
        {
            writer.Write(MessageID);
            return 8;
        }

        public void Deserialize(BinaryReader reader)
        {
            MessageID=reader.ReadInt32();
        }
    }
}