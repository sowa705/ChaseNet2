using System.IO;
using ChaseNet2.Serialization;
using ProtoBuf;

namespace ChaseNet2.Transport.Messages
{
    [ProtoContract]
    public class SplitMessagePart
    {
        [ProtoMember(1)]
        public ulong OriginalMessageId { get; set; }
        [ProtoMember(2)]
        public ulong Channel { get; set; }
        [ProtoMember(3)]
        public MessageType OriginalMessageType { get; set; }
        [ProtoMember(4)]
        public int PartNumber { get; set; }
        [ProtoMember(5)]
        public int TotalParts { get; set; }
        [ProtoMember(6)]
        public int PartSize { get; set; }
        [ProtoMember(7)]
        public byte[] Data { get; set; }
    }
}