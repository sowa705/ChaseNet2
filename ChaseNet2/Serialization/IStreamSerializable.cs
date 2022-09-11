using System.IO;

namespace ChaseNet2.Serialization
{
    public interface IStreamSerializable
    {
        public void Serialize(object obj, BinaryWriter writer);
        public void Deserialize(BinaryReader reader);
    }
}