using ChaseNet2.Serialization;

public class FilePartSpec : IStreamSerializable
{
    public long Offset { get; set; }
    public long Size { get; set; }
    public byte[] Hash { get; set; }
    public int Serialize(BinaryWriter writer)
    {
        writer.Write(Offset);
        writer.Write(Size);
        writer.Write(Hash.Length);
        writer.Write(Hash);
        
        return 8 + 8 + 4 + Hash.Length;
    }

    public void Deserialize(BinaryReader reader)
    {
        Offset = reader.ReadInt64();
        Size = reader.ReadInt64();
        int hashLength = reader.ReadInt32();
        Hash = reader.ReadBytes(hashLength);
    }
}