using System.IO;
using ChaseNet2.Serialization;

namespace ChaseNet2.Transport.Messages
{
    public class Pong:IStreamSerializable
    {
        public int RandomNumber { get; set; }
        public int Serialize(BinaryWriter writer)
        {
            writer.Write(RandomNumber);
            return 4;
        }

        public void Deserialize(BinaryReader reader)
        {
            RandomNumber=reader.ReadInt32();
        }
    }
}