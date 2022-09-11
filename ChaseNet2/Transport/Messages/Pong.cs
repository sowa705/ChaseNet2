using System.IO;
using ChaseNet2.Serialization;

namespace ChaseNet2.Transport.Messages
{
    public class Pong:IStreamSerializable
    {
        public int RandomNumber { get; set; }
        public void Serialize(object obj, BinaryWriter writer)
        {
            writer.Write(RandomNumber);
        }

        public void Deserialize(BinaryReader reader)
        {
            RandomNumber=reader.ReadInt32();
        }
    }
}