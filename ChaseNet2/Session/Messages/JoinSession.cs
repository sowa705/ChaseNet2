using System.IO;
using ChaseNet2.Extensions;
using ChaseNet2.Serialization;

namespace ChaseNet2.Session.Messages
{
    public class JoinSession:IStreamSerializable
    {
        public string SessionName { get; set; }
        public int Serialize(BinaryWriter writer)
        {
            return writer.WriteUTF8String(SessionName);
        }

        public void Deserialize(BinaryReader reader)
        {
            SessionName = reader.ReadUTF8String();
        }
    }
}