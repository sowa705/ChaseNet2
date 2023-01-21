using System.Security.Cryptography;
using ChaseNet2.Extensions;
using ChaseNet2.Serialization;
using ProtoBuf;
using Serilog;

[ProtoContract]
public class FileSpec
{
    [ProtoMember(1)]
    public string FileName { get; set; }
    [ProtoMember(2)]
    public int PartSize { get; set; }
    [ProtoMember(3)]
    public List<FilePartSpec> Parts { get; set; } = new List<FilePartSpec>();

    public static async Task<FileSpec> Create(string path)
    {
        var spec = new FileSpec();
        spec.FileName = path.Split('\\')[^1];

        //prepare parts

        using FileStream fs = new FileStream(path, FileMode.Open);

        var parts = new List<FilePartSpec>();
        spec.Parts = parts;

        var partSize = 1024 * 256; //256kB
        spec.PartSize = partSize;

        while (fs.Position < fs.Length)
        {
            Log.Information("Creating file spec part {part} of {total}", parts.Count + 1, (int)Math.Ceiling((double)fs.Length / partSize));
            var part = new FilePartSpec();
            part.Offset = fs.Position;
            part.Size = Math.Min(partSize, fs.Length - fs.Position);

            part.Hash = await HashFilePart(fs, part.Size);

            parts.Add(part);
        }

        return spec;
    }

    public static async Task<byte[]> HashFilePart(FileStream fs, long partPartSize)
    {
        byte[] buffer = new byte[partPartSize];
        await fs.ReadAsync(buffer, 0, (int)partPartSize);
        return SHA256.Create().ComputeHash(buffer);
    }
}