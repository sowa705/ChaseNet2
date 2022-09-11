using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Text;

namespace ChaseNet2.Serialization
{
    public class SerializationManager
    {
        public Dictionary<ulong, Type> TypeIDs= new Dictionary<ulong, Type>();
        
        public void RegisterType(Type type, bool useFullName=true)
        {
            if (!typeof(IStreamSerializable).IsAssignableFrom(type))
            {
                throw new ArgumentException("Type must implement IStreamSerializable", "type");
            }
            
            string name = type.FullName;
            if (!useFullName)
                name = type.Name;
            
            // compute unique ID with sha1
            var bytes=SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(name));
            
            var id=BitConverter.ToUInt64(bytes, 0); // first 8 bytes of sha1 hash, should be unique enough
            Console.WriteLine("Registering type {0} with ID {1}", name, id);
            
            TypeIDs.Add(id, type);
        }
        /// <summary>
        /// Writes an object to the binary writer, fails if the object does not implement IStreamSerializable or is not registered, returns written bytes
        /// </summary>
        public int Serialize<T>(T obj,BinaryWriter writer)
        {
            var type = obj.GetType();
            var id = TypeIDs.FirstOrDefault(x => x.Value == type).Key;

            if (id==0)
            {
                throw new ArgumentException("Type not registered", "type");
            }
            // write type ID
            writer.Write(BitConverter.GetBytes(id));
            // write data
            return (obj as IStreamSerializable).Serialize(obj, writer)+8; //message+8 bytes for type ID
        }
        
        public object Deserialize(BinaryReader reader)
        {
            // read type ID
            var id=BitConverter.ToUInt64(reader.ReadBytes(8), 0);
            var type = TypeIDs[id];
            // read data
            var obj = Activator.CreateInstance(type);
            (obj as IStreamSerializable).Deserialize(reader);
            return obj;
        }
        
        public T Deserialize<T>(BinaryReader reader) where T : class
        {
            // read type ID
            var id=BitConverter.ToUInt64(reader.ReadBytes(8), 0);
            var type = TypeIDs[id];
            // read data
            var obj = Activator.CreateInstance(type);
            (obj as IStreamSerializable).Deserialize(reader);
            return (obj as T);
        }
    }
}