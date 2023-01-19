using System.IO;
using ChaseNet2.Serialization;

namespace ChaseNet2.Transport.Messages
{
    public class SplitMessagePart : IStreamSerializable
    {
        public ulong OriginalMessageId { get; set; }
        public ulong Channel { get; set; }
        public MessageType OriginalMessageType { get; set; }
        public int PartNumber { get; set; }
        public int TotalParts { get; set; }
        public int PartSize { get; set; }
        public byte[] Data { get; set; }
        
        public int Serialize(BinaryWriter writer)
        {
            writer.Write(OriginalMessageId);
            writer.Write(Channel);
            writer.Write((byte)OriginalMessageType);
            writer.Write(PartNumber);
            writer.Write(TotalParts);
            writer.Write(PartSize);
            writer.Write(Data.Length);
            writer.Write(Data);
            
            return sizeof(ulong) + sizeof(ulong) + 1 + sizeof(int) + sizeof(int) + sizeof(int) + sizeof(int) + Data.Length;
        }
        
        public void Deserialize(BinaryReader reader)
        {
            OriginalMessageId = reader.ReadUInt64();
            Channel = reader.ReadUInt64();
            OriginalMessageType = (MessageType)reader.ReadByte();
            PartNumber = reader.ReadInt32();
            TotalParts = reader.ReadInt32();
            PartSize = reader.ReadInt32();
            Data = reader.ReadBytes(reader.ReadInt32());
        }
    }
}