using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ChaseNet2.Session.Messages;
using ChaseNet2.Transport.Messages;
using Org.BouncyCastle.Crypto;
using ProtoBuf;
using ProtoBuf.Meta;
using Serilog;

namespace ChaseNet2.Serialization
{
    public class SerializationManager
    {
        Dictionary<ulong, Type> TypeIDs = new Dictionary<ulong, Type>();

        RuntimeTypeModel _runtimeTypeModel;

        public SerializationManager()
        {
            _runtimeTypeModel = RuntimeTypeModel.Create();
        }

        public ulong RegisterType<T>(bool useFullName = true)
        {
            var type = typeof(T);
            return RegisterType(type, useFullName);
        }

        public ulong RegisterType(Type type, bool useFullName = true)
        {
            if (type.GetCustomAttribute<ProtoContractAttribute>() == null)
            {
                throw new ArgumentException("Type must have the ProtoContract attribute");
            }

            string name = type.FullName;
            if (!useFullName)
                name = type.Name;

            // compute unique ID with sha1
            var bytes = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(name));

            var id = BitConverter.ToUInt64(bytes, 0); // first 8 bytes of sha256 hash, should be unique enough
                                                      // Console.WriteLine("Registering type {0} with ID {1}", name, id);

            TypeIDs.Add(id, type);
            return id;
        }
        public ulong RegisterSurrogate<T, T2>(bool useFullName = true)
        {
            var type = typeof(T);
            var surrogate = typeof(T2);

            if (surrogate.GetCustomAttribute<ProtoContractAttribute>() == null)
            {
                throw new ArgumentException("Surrogate type must have the ProtoContract attribute");
            }
            string name = type.FullName;
            if (!useFullName)
                name = type.Name;
            // compute unique ID with sha1
            
            var bytes = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(name));

            var id = BitConverter.ToUInt64(bytes, 0); // first 8 bytes of sha256 hash, should be unique enough
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

            _runtimeTypeModel.Add<AsymmetricKeyParameter>().SetSurrogate(typeof(AsymmetricKeyParameterSurrogate));
            _runtimeTypeModel.Add<IPEndPoint>().SetSurrogate(typeof(IPEndPointSurrogate));
        }

        /// <summary>
        /// Writes an object to the binary writer, fails if the object does not implement IStreamSerializable or is not registered, returns written bytes
        /// </summary>
        public byte[] Serialize<T>(T obj)
        {
            BinaryWriter writer = new BinaryWriter(new MemoryStream());
            var type = obj.GetType();
            var id = TypeIDs.FirstOrDefault(x => x.Value == type).Key;

            if (id == 0)
            {
                id = RegisterType(obj.GetType());
            }
            // write type ID
            writer.Write(BitConverter.GetBytes(id));

            // write data
            var protostream = new MemoryStream();
            _runtimeTypeModel.Serialize(protostream, obj);

            var data = protostream.ToArray();
            writer.Write(data.Length);
            writer.Write(data);

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
            var id = BitConverter.ToUInt64(reader.ReadBytes(8), 0);

            if (!TypeIDs.ContainsKey(id))
            {
                throw new Exception("Cannot deserialize object with an unknown type ID " + id);
            }

            var type = TypeIDs[id];

            var length = reader.ReadInt32();
            var data = reader.ReadBytes(length);

            var obj = _runtimeTypeModel.Deserialize(type, new MemoryStream(data));

            if (obj is null)
            {
                throw new Exception("Failed to deserialize type " + type.FullName);
            }

            return obj;
        }

        public T Deserialize<T>(BinaryReader reader) where T : class
        {
            return Deserialize(reader) as T;
        }
    }
}