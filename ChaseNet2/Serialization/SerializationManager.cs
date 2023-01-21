using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ChaseNet2.Session.Messages;
using ChaseNet2.Transport.Messages;
using Serilog;

namespace ChaseNet2.Serialization
{
    public class SerializationManager
    {
        Dictionary<ulong, Type> TypeIDs = new Dictionary<ulong, Type>();
        
        /// <summary>
        /// Copies the data to an array when writing to ensure the length is correct.
        /// This is a bit slower but provides useful information for debugging
        /// </summary>
        public bool CopyMode = true;
        
        public ulong RegisterType<T>(bool useFullName=true)
        {
            var type = typeof(T);
            return RegisterType(type, useFullName);
        }
        
        public ulong RegisterType(Type type, bool useFullName=true)
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
           // Console.WriteLine("Registering type {0} with ID {1}", name, id);
            
            TypeIDs.Add(id, type);
            return id;
        }

        public void RegisterChaseNetTypes()
        {
            RegisterType(typeof(ConnectionRequest));
            RegisterType(typeof(ConnectionResponse));
            RegisterType(typeof(Ack));
            RegisterType(typeof(Ping));
            RegisterType(typeof(Pong));
            RegisterType(typeof(SplitMessagePart));
            
            RegisterType(typeof(JoinSession));
            RegisterType(typeof(JoinSessionResponse));
            RegisterType(typeof(SessionUpdate));
        }
        
        /// <summary>
        /// Writes an object to the binary writer, fails if the object does not implement IStreamSerializable or is not registered, returns written bytes
        /// </summary>
        public byte[] Serialize<T>(T obj)
        {
            BinaryWriter writer = new BinaryWriter(new MemoryStream());
            var type = obj.GetType();
            var id = TypeIDs.FirstOrDefault(x => x.Value == type).Key;

            if (id==0)
            {
                id=RegisterType(obj.GetType());
            }
            // write type ID
            writer.Write(BitConverter.GetBytes(id));
            // write data
            
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            var writtenBytes = ((IStreamSerializable)obj).Serialize(bw);
                
            var data = ms.ToArray();
            writer.Write(data.Length);
            writer.Write(data);

            if (writtenBytes!= data.Length)
            {
                throw new Exception("Written byte count does not match actual length for type " + type.FullName);
            }
                
            return ((MemoryStream)writer.BaseStream).ToArray();
        }

        public object Deserialize(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);

            return Deserialize(br);
        }
        
        public object Deserialize(BinaryReader reader)
        {
            // read type ID
            var id=BitConverter.ToUInt64(reader.ReadBytes(8), 0);
            var type = TypeIDs[id];
            // read data
            var obj = Activator.CreateInstance(type);
            
            if (CopyMode)
            {
                var length = reader.ReadInt32();
                var data = reader.ReadBytes(length);
                
                using var ms = new MemoryStream(data);
                using var br = new BinaryReader(ms);

                try
                {
                    ((IStreamSerializable)obj).Deserialize(br);
                }
                catch (Exception e)
                {
                    Log.Logger.Error("Error deserializing type {0} with ID {1} because of {2}", type.FullName, id, e.Message);
                    throw;
                }
                
                return obj;
            }
            
            (obj as IStreamSerializable)!.Deserialize(reader);
            return obj;
        }
        
        public T Deserialize<T>(BinaryReader reader) where T : class
        {
            return Deserialize(reader) as T;
        }
    }
}