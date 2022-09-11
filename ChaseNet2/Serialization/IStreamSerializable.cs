using System.IO;
using System.Text;

namespace ChaseNet2.Serialization
{
    public interface IStreamSerializable
    {
        public int Serialize(BinaryWriter writer);
        public void Deserialize(BinaryReader reader);
    }
}