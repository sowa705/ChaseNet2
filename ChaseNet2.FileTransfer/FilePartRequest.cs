using ChaseNet2.Extensions;
using ChaseNet2.Serialization;
using ProtoBuf;

[ProtoContract]
public class FilePartRequest
{
    [ProtoMember(1)]
    public string FileName { get; set; }
    [ProtoMember(2)]
    public long Offset { get; set; }
    [ProtoMember(3)]
    public int Length { get; set; }
}