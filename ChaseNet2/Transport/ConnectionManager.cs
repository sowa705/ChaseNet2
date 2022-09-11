using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using ChaseNet2.Serialization;
using ChaseNet2.Transport.Messages;

namespace ChaseNet2
{
    public class ConnectionManager
    {
        ECDiffieHellmanCng _ecdh;

        public ECDiffieHellmanPublicKey PublicKey { get => _ecdh.PublicKey; }

        private UdpClient _client;
        public List<Connection> Connections { get; private set; }

        private Random rng;
        
        public bool AcceptNewConnections { get; set; }
        
        public NetworkStatistics Statistics { get; private set; }
        
        public SerializationManager Serializer { get; private set; }

        public ConnectionManager(int? port = null)
        {
            _ecdh = new ECDiffieHellmanCng();

            _client = port == null ? new UdpClient() : new UdpClient(port.Value);
            
            Connections = new List<Connection>();

            Statistics = new NetworkStatistics();
            Serializer = new SerializationManager();
            RegisterInternalSerializableTypes();
            
            rng = new Random();
        }

        void RegisterInternalSerializableTypes()
        {
            Serializer.RegisterType(typeof(ConnectionRequest));
            Serializer.RegisterType(typeof(Ack));
            Serializer.RegisterType(typeof(Ping));
            Serializer.RegisterType(typeof(Pong));
        }

        public Connection CreateConnection(IPEndPoint targetEndpoint, ECDiffieHellmanPublicKey targetPublicKey)
        {
            var c = new Connection(this, targetEndpoint, targetPublicKey);
            Connections.Add(c);

            Statistics.ConnectionCount = Connections.Count;

            return c;
        }

        public void Update()
        {
            Stopwatch stopwatch= new Stopwatch();
            
            stopwatch.Start();
            
            while (_client.Available>0)
            {
                ProcessIncomingPacket();
            }
            
            foreach (var c in Connections)
            {
                c.Update();
            }
            
            stopwatch.Stop();

            Statistics.AverageUpdateTime = (Statistics.AverageUpdateTime+stopwatch.Elapsed)/2;
        }

        void ProcessIncomingPacket()
        {
            var remoteEP = new IPEndPoint(IPAddress.Any, 0);
            var data = _client.Receive(ref remoteEP);
            
            Statistics.BytesReceived += data.Length;
            Statistics.PacketsReceived ++;
                
            var c = Connections.Find(x => x.RemoteEndpoint.Equals(remoteEP));
            using var ms = new MemoryStream(data);
            
            if (c==null)
            {
                Console.WriteLine("Received packet from an unrecognized endpoint");

                if (AcceptNewConnections)
                {
                    BinaryReader reader = new BinaryReader(ms);
                    var messageType = reader.ReadInt32();

                    if (messageType!=0xADDDDDD) //connection request magic number
                    {
                        Console.WriteLine("Received packet from an unrecognized endpoint, but it wasn't a connect message");
                        return;
                    }
                    
                    try
                    {
                        ConnectionRequest request = Serializer.Deserialize<ConnectionRequest>(reader);
                        
                        CreateConnection(remoteEP, request.PublicKey);
                        Console.WriteLine("Client connected");
                    }
                    catch
                    {
                        Console.WriteLine("Client tried to connect with an invalid key");
                    }
                }
                return;
            }
            
            c.ReadInputStream(ms);
        }
        
        public byte[] ComputeSharedSecretKey(ECDiffieHellmanPublicKey remotePublicKey)
        {
            return _ecdh.DeriveKeyFromHash(remotePublicKey,HashAlgorithmName.SHA256);
        }
        
        public Aes CreateAes(byte[] key,byte[] iv)
        {
            var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            return aes;
        }

        public byte[] GenerateInitializationVector()
        {
            var iv = new byte[16];
            rng.NextBytes(iv);
            return iv;
        }

        public void SendPacket(byte[] array, IPEndPoint remoteEndpoint)
        {
            _client.Send(array, array.Length, remoteEndpoint);
            
            Statistics.PacketsSent++;
            Statistics.BytesSent += array.Length;
        }
    }
}