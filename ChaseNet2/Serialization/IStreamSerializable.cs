using System.IO;

namespace ChaseNet2.Serialization
{
    public interface IStreamSerializable
    {
        public int Serialize(object obj, BinaryWriter writer);
        public void Deserialize(BinaryReader reader);
    }
}