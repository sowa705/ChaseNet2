using ChaseNet2.Extensions;
using ChaseNet2.Serialization;
using ProtoBuf;

[ProtoContract]
public class FilePartResponse
{
    [ProtoMember(1)]
    public string FileName { get; set; }
    [ProtoMember(2)]
    public long Offset { get; set; }
    [ProtoMember(3)]
    public byte[] Data { get; set; }
}