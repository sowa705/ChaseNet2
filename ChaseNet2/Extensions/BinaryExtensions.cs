using System.IO;
using System.Text;

namespace ChaseNet2.Extensions
{
    public static class BinaryExtensions
    {
        
        public static int WriteUTF8String(this BinaryWriter writer, string str)
        {
            var bytes = Encoding.UTF8.GetBytes(str);
            writer.Write(bytes.Length);
            writer.Write(bytes);
            
            return 4+bytes.Length;
        }
        public static string ReadUTF8String(this BinaryReader reader)
        {
            var length = reader.ReadInt32();
            var bytes = reader.ReadBytes(length);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}