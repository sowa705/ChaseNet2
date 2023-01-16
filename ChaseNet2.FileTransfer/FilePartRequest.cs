using ChaseNet2.Extensions;
using ChaseNet2.Serialization;

public class FilePartRequest : IStreamSerializable
{
    public string FileName { get; set; }
    public int Offset { get; set; }
    public int Length { get; set; }
    public int Serialize(BinaryWriter writer)
    {
        int size = writer.WriteUTF8String(FileName);
        writer.Write(Offset);
        writer.Write(Length);
        return size+4+4;
    }

    public void Deserialize(BinaryReader reader)
    {
        FileName = reader.ReadUTF8String();
        Offset = reader.ReadInt32();
        Length = reader.ReadInt32();
    }
}