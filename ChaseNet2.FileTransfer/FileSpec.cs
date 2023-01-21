using System.Security.Cryptography;
using ChaseNet2.Extensions;
using ChaseNet2.Serialization;

public class FileSpec : IStreamSerializable
{
    public string FileName { get; set; }
    public List<FilePartSpec> Parts { get; set; } = new List<FilePartSpec>();
    
    public static async Task<FileSpec> Create(string path)
    {
        var spec = new FileSpec();
        spec.FileName = path.Split('\\')[^1];
        
        //prepare parts
        
        FileStream fs = new FileStream(path, FileMode.Open);
        
        var parts = new List<FilePartSpec>();
        spec.Parts = parts;
        
        var partSize = 1024 * 1024 * 1; //1MB

        while (fs.Position < fs.Length)
        {
            var part = new FilePartSpec();
            part.Offset = fs.Position;
            part.Size = Math.Min(partSize, fs.Length - fs.Position);
            
            part.Hash = await HashFilePart(fs, part.Size);
            
            parts.Add(part);
        }
        
        return spec;
    }

    private static async Task<byte[]> HashFilePart(FileStream fs, long partPartSize)
    {
        byte[] buffer = new byte[partPartSize];
        await fs.ReadAsync(buffer, 0, (int) partPartSize);
        return SHA256.Create().ComputeHash(buffer);
    }

    public int Serialize(BinaryWriter writer)
    {
        int strSize = writer.WriteUTF8String(FileName);
        int partsSize = Parts.Serialize(writer);
        return strSize + partsSize;
    }

    public void Deserialize(BinaryReader reader)
    {
        FileName = reader.ReadUTF8String();
        Parts.Deserialize(reader);
    }
}