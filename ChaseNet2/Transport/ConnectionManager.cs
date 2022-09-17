using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ChaseNet2.Serialization;
using ChaseNet2.Transport.Messages;

namespace ChaseNet2.Transport
{
    public class ConnectionManager
    {
        ECDiffieHellmanCng _ecdh;

        public ECDiffieHellmanPublicKey PublicKey { get => _ecdh.PublicKey; }
        private UdpClient _client;
        public List<Connection> Connections { get; private set; }
        
        public List<ConnectionHandler> Handlers { get; private set; }

        private Random rng;
        public bool AcceptNewConnections { get; set; }
        public NetworkStatistics Statistics { get; private set; }
        public SerializationManager Serializer { get; private set; }
        
        /// <summary>
        /// The rate at which background thread will update connections in updates per second.
        /// For servers it is re
        /// </summary>
        public float TargetUpdateRate { get; set; } = 30;

        public ConnectionManager(int? port = null)
        {
            _ecdh = new ECDiffieHellmanCng();

            _client = port == null ? new UdpClient() : new UdpClient(port.Value);

            Connections = new List<Connection>();
            Handlers = new List<ConnectionHandler>();

            Statistics = new NetworkStatistics();
            Serializer = new SerializationManager();
            Serializer.RegisterChaseNetTypes();
            
            rng = new Random();
        }
        public Connection AttachConnection(ConnectionTarget target)
        {
            var c = new Connection(this, target);
            Connections.Add(c);

            Statistics.ConnectionCount = Connections.Count;

            foreach (var h in Handlers)
            {
                h.OnManagerConnect(c);
            }

            return c;
        }
        public Connection CreateConnection(IPEndPoint endPoint, ECDiffieHellmanPublicKey publicKey)
        {
            var bytes= new byte[8];
            rng.NextBytes(bytes);
            var id = BitConverter.ToUInt64(bytes, 0);
            
            var c = new Connection(this, new ConnectionTarget() { EndPoint = endPoint, PublicKey = publicKey, ConnectionID = id });
            Connections.Add(c);

            Statistics.ConnectionCount = Connections.Count;
            
            Console.WriteLine($"Created connection to {endPoint} with id {id}");

            return c;
        }
        public void StartBackgroundThread()
        {
            Task.Run(BackgroundThread);
        }
        
        public void AttachHandler(ConnectionHandler connectionHandler)
        {
            Handlers.Add(connectionHandler);
        }
        
        public void DetachHandler(ConnectionHandler connectionHandler)
        {
            Handlers.Remove(connectionHandler);
        }
        
        async Task BackgroundThread()
        {
            while (true)
            {
                var updateTime = await Update();

                var sleepTime = TimeSpan.FromSeconds(1f / TargetUpdateRate);

                if (updateTime < sleepTime)
                {
                    sleepTime -= updateTime;
                }
                
                await Task.Delay(sleepTime);
            }
        }

        public async Task<TimeSpan> Update()
        {
            Stopwatch stopwatch= new Stopwatch();
            stopwatch.Start();
            
            while (_client.Available>0)
            {
                ProcessIncomingPacket();
            }
            
            // update all connections on async worker threads

            await Task.WhenAll(Connections.Select(x => Task.Run( x.Update )));

            foreach (var handler in Handlers)
            {
                foreach (var connection in Connections.Where(x=>handler.ShouldHandle(x.ConnectionId)))
                {
                    handler.ConnectionUpdate(connection);
                }
            }
            
            stopwatch.Stop();

            Statistics.AverageUpdateTime = (Statistics.AverageUpdateTime+stopwatch.Elapsed)/2;
            
            return stopwatch.Elapsed;
        }

        void ProcessIncomingPacket()
        {
            var remoteEP = new IPEndPoint(IPAddress.Any, 0);
            var data = _client.Receive(ref remoteEP);
            
            var targetConnection = BitConverter.ToUInt64(data, 0);
            
            Statistics.BytesReceived += data.Length;
            Statistics.PacketsReceived ++;
                
            var c = Connections.Find(x => x.ConnectionId == targetConnection);
            
            using var ms = new MemoryStream(data, 8, data.Length - 8); // skip first 8 bytes
            
            if (targetConnection==0xADDDDDD)
            {
                Console.WriteLine($"Received connection request from {remoteEP}");

                if (AcceptNewConnections)
                {
                    BinaryReader reader = new BinaryReader(ms);
                    
                    try
                    {
                        ConnectionRequest request = Serializer.Deserialize<ConnectionRequest>(reader);
                        Console.WriteLine("Client connected with id: " + request.ConnectionId);

                        AttachConnection(new ConnectionTarget() {EndPoint = remoteEP, PublicKey = request.PublicKey, ConnectionID = request.ConnectionId});
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
        
        public byte[] ComputeSharedSecretKey(ECDiffieHellmanPublicKey remotePublicKey, ulong connectionID)
        {
            return _ecdh.DeriveKeyFromHash(remotePublicKey, HashAlgorithmName.SHA256, BitConverter.GetBytes(connectionID),null);
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

        public void SendPacket(byte[] array, IPEndPoint remoteEndpoint, ulong connectionId)
        {
            var data = new byte[array.Length + 8];
            BitConverter.GetBytes(connectionId).CopyTo(data, 0);
            array.CopyTo(data, 8);
            
            _client.Send(data, data.Length, remoteEndpoint);
            
            Statistics.BytesSent += data.Length;
            Statistics.PacketsSent ++;
        }
    }
}