using ChaseNet2.Extensions;
using ChaseNet2.Serialization;

public class FilePartResponse:IStreamSerializable
{
    public string FileName { get; set; }
    public long Offset { get; set; }
    public byte[] Data { get; set; }
    public int Serialize(BinaryWriter writer)
    {
        int size = writer.WriteUTF8String(FileName);
        
        writer.Write(Offset);
        writer.Write(Data.Length);
        writer.Write(Data);

        return size+8+4+Data.Length;
    }

    public void Deserialize(BinaryReader reader)
    {
        FileName = reader.ReadUTF8String();
        Offset = reader.ReadInt32();
        var length = reader.ReadInt32();
        Data = reader.ReadBytes(length);
    }
}