using ChaseNet2.Serialization;
using ProtoBuf;

[ProtoContract]
public class FilePartSpec
{
    [ProtoMember(1)]
    public long Offset { get; set; }
    [ProtoMember(2)]
    public long Size { get; set; }
    [ProtoMember(3)]
    public byte[] Hash { get; set; }
}