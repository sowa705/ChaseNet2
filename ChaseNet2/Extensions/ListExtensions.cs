using System.Collections.Generic;
using System.IO;
using ChaseNet2.Serialization;

namespace ChaseNet2.Extensions
{
    public static class ListExtensions
    {
        public static int Serialize<T>(this List<T> list, BinaryWriter writer) where T: IStreamSerializable
        {
            int count = list.Count;
            int bytes = 4;
            
            writer.Write(count);
            
            foreach (var item in list)
            {
                bytes += item.Serialize(writer);
            }
            
            return bytes;
        }
        
        public static void Deserialize<T>(this List<T> list, BinaryReader reader) where T: IStreamSerializable, new()
        {
            list.Clear();
            
            int count = reader.ReadInt32();

            for (int i = 0; i < count; i++)
            {
                T item = new T();
                item.Deserialize(reader);
                list.Add(item);
            }
        }
    }
}