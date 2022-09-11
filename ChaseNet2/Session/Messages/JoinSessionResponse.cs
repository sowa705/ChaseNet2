using System.IO;
using ChaseNet2.Extensions;
using ChaseNet2.Serialization;

namespace ChaseNet2.Session.Messages
{
    public class JoinSessionResponse:IStreamSerializable
    {
        public bool Accepted { get; set; }
        public int Serialize(BinaryWriter writer)
        {
            writer.Write(Accepted);
            return 1;
        }

        public void Deserialize(BinaryReader reader)
        {
            Accepted=reader.ReadBoolean();
        }
    }
}