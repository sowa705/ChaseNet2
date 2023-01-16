using System.Security.Cryptography;

public class FileSpec
{
    public string FileName { get; set; }
    public List<FilePartSpec> Parts { get; set; }
    
    public static async Task<FileSpec> Create(string path)
    {
        var spec = new FileSpec();
        spec.FileName = path.Split('\\')[^1];
        
        //prepare parts
        
        FileStream fs = new FileStream(path, FileMode.Open);
        
        var parts = new List<FilePartSpec>();
        spec.Parts = parts;
        
        var partSize = 32768;

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
}