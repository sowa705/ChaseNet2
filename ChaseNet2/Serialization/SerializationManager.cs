using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ChaseNet2.Session.Messages;
using ChaseNet2.Transport.Messages;
using Org.BouncyCastle.Crypto;
using ProtoBuf;
using ProtoBuf.Meta;

namespace ChaseNet2.Serialization
{
    /// <summary>
    /// Maps message types to wire IDs and serializes them with protobuf-net.
    /// Thread-safe: types may be registered from any thread while other threads serialize and deserialize.
    /// Registration publishes a new copy of the lookup tables (copy-on-write), so readers never take a lock.
    /// </summary>
    public class SerializationManager
    {
        private readonly object _registrationLock = new object();

        // Copy-on-write snapshots; replaced (never mutated) under _registrationLock.
        private volatile Dictionary<ulong, Type> _typesById = new Dictionary<ulong, Type>();
        private volatile Dictionary<Type, ulong> _idsByType = new Dictionary<Type, ulong>();

        private readonly RuntimeTypeModel _runtimeTypeModel;

        public SerializationManager()
        {
            _runtimeTypeModel = RuntimeTypeModel.Create();
        }

        public ulong RegisterType<T>(bool useFullName = true)
        {
            return RegisterType(typeof(T), useFullName);
        }

        /// <summary>
        /// Registers a type and returns its wire ID. Registering the same type again returns the existing ID.
        /// </summary>
        public ulong RegisterType(Type type, bool useFullName = true)
        {
            if (type.GetCustomAttribute<ProtoContractAttribute>() == null)
            {
                throw new ArgumentException("Type must have the ProtoContract attribute");
            }

            string name = useFullName ? type.FullName! : type.Name;

            // first 8 bytes of a sha256 hash of the name, should be unique enough
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(name));
            var id = BitConverter.ToUInt64(bytes, 0);

            lock (_registrationLock)
            {
                if (_typesById.TryGetValue(id, out var existing))
                {
                    if (existing == type)
                    {
                        return id;
                    }
                    throw new ArgumentException($"Type {type.FullName} has the same wire ID as already registered type {existing.FullName}");
                }

                var typesById = new Dictionary<ulong, Type>(_typesById) { [id] = type };
                var idsByType = new Dictionary<Type, ulong>(_idsByType);
                // a type registered under both its short and full name keeps the first ID for sending
                idsByType.TryAdd(type, id);

                _typesById = typesById;
                _idsByType = idsByType;
            }

            return id;
        }

        public void RegisterSurrogate<T, T2>(bool useFullName = true)
        {
            lock (_registrationLock)
            {
                _runtimeTypeModel.Add<T>().SetSurrogate(typeof(T2));
            }
        }

        public bool IsRegistered(Type type) => _idsByType.ContainsKey(type);

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

            RegisterSurrogate<AsymmetricKeyParameter, AsymmetricKeyParameterSurrogate>();
            RegisterSurrogate<IPEndPoint, IPEndPointSurrogate>();
        }

        /// <summary>
        /// Serializes an object to bytes (type ID, length, protobuf payload). Unregistered types are registered on the fly.
        /// </summary>
        public byte[] Serialize<T>(T obj)
        {
            if (obj is null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            var type = obj.GetType();
            if (!_idsByType.TryGetValue(type, out var id))
            {
                id = RegisterType(type);
            }

            var protostream = new MemoryStream();
            _runtimeTypeModel.Serialize(protostream, obj);

            var output = new MemoryStream((int)protostream.Length + 12);
            var writer = new BinaryWriter(output);
            writer.Write(id);
            writer.Write((int)protostream.Length);
            writer.Write(protostream.GetBuffer(), 0, (int)protostream.Length);
            writer.Flush();

            return output.ToArray();
        }

        public object Deserialize(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);

            return Deserialize(br);
        }

        public object Deserialize(BinaryReader reader)
        {
            var id = reader.ReadUInt64();

            if (!_typesById.TryGetValue(id, out var type))
            {
                throw new Exception("Cannot deserialize object with an unknown type ID " + id);
            }

            var length = reader.ReadInt32();
            var data = reader.ReadBytes(length);
            if (data.Length != length)
            {
                throw new EndOfStreamException("Message payload is truncated");
            }

            var obj = _runtimeTypeModel.Deserialize(type, new MemoryStream(data));

            if (obj is null)
            {
                throw new Exception("Failed to deserialize type " + type.FullName);
            }

            return obj;
        }

        public T Deserialize<T>(BinaryReader reader) where T : class
        {
            return (Deserialize(reader) as T)!;
        }
    }
}
