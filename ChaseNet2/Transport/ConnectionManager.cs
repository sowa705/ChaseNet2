﻿using System;
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
using Org.BouncyCastle.Crypto;
using Serilog;

namespace ChaseNet2.Transport
{
    public class ConnectionManager
    {
        AsymmetricCipherKeyPair _keyPair;
        public AsymmetricKeyParameter PublicKey { get => _keyPair.Public; }
        private UdpClient _client;
        public List<Connection> Connections { get; private set; }
        
        public List<ConnectionHandler> Handlers { get; private set; }

        private Random rng;
        public bool AcceptNewConnections { get; set; }
        public NetworkStatistics Statistics { get; private set; }
        public SerializationManager Serializer { get; private set; }

        /// <summary>
        /// The rate at which background thread will update connections in updates per second.
        /// Recommended value on tracker servers is 20, clients can use 60-120.
        /// </summary>
        public float TargetUpdateRate { get; set; } = 30;

        int tickCount = 0;
        
        long lastSentBytes = 0;
        long lastReceivedBytes = 0;

        public ConnectionManager(int? port = null)
        {
            _keyPair = CryptoHelper.GenerateKeyPair();

            _client = port == null ? new UdpClient() : new UdpClient(port.Value);

            Connections = new List<Connection>();
            Handlers = new List<ConnectionHandler>();

            Statistics = new NetworkStatistics();
            Serializer = new SerializationManager();
            Serializer.RegisterChaseNetTypes();
            
            rng = new Random();
        }
        public async Task<Connection> AttachConnectionAsync(ConnectionTarget target)
        {
            var c = new Connection(this, target);
            Connections.Add(c);

            Statistics.ConnectionCount = Connections.Count;

            foreach (var h in Handlers)
            {
                await h.OnManagerConnect(c);
            }

            return c;
        }
        public void RemoveConnection(ulong connectionId)
        {
            var c = Connections.Find(c => c.ConnectionId == connectionId);
            Connections.Remove(c);
            Statistics.ConnectionCount = Connections.Count;
        }
        public Connection CreateConnection(IPEndPoint endPoint)
        {
            var bytes= new byte[8];
            rng.NextBytes(bytes);
            var id = BitConverter.ToUInt64(bytes, 0);
            
            var c = new Connection(this, new ConnectionTarget() { EndPoint = endPoint, PublicKey = null, ConnectionId = id });
            Connections.Add(c);

            Statistics.ConnectionCount = Connections.Count;

            Log.Logger.Information("Created connection to {0} with id {1}", endPoint, id);

            return c;
        }
        public void StartBackgroundThread()
        {
            Task.Run(BackgroundThread).ContinueWith((t) =>
            {
                Log.Logger.Error("Background thread crashed: {0}", t.Exception);
            });
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
            tickCount++;
            
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
            if (Connections.Count>0)
            {
                Statistics.AveragePing = Connections.Average(x => x.AveragePing);
            }

            if (tickCount>=TargetUpdateRate)
            {
                tickCount = 0;
                
                Statistics.BitsSentPerSecond = (Statistics.BytesSent - lastSentBytes)*8;
                Statistics.BitsReceivedPerSecond = (Statistics.BytesReceived - lastReceivedBytes)*8;

                lastSentBytes = Statistics.BytesSent;
                lastReceivedBytes = Statistics.BytesReceived;
            }

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
            
            if (targetConnection==0xADDDDDDDD)
            {
                Log.Logger.Information("Received connection request from {EndPoint}", remoteEP);

                if (AcceptNewConnections)
                {
                    BinaryReader reader = new BinaryReader(ms);
                    
                    try
                    {
                        ConnectionRequest request = Serializer.Deserialize<ConnectionRequest>(reader);

                        if (Connections.Find(x => x.ConnectionId == request.ConnectionId) != null)
                        {
                            return;
                        }

                        var attachTask = AttachConnectionAsync(new ConnectionTarget()
                        {
                            EndPoint = remoteEP, PublicKey = request.PublicKey, ConnectionId = request.ConnectionId
                        });
                        attachTask.Wait();
                        var connection = attachTask.Result;
                        
                        connection.SendConnectionResponse();

                        Log.Logger.Information("Attached a new connection from {EndPoint} with id {ConnectionId}", remoteEP, request.ConnectionId);
                    }
                    catch
                    {
                        Log.Logger.Warning("Failed to deserialize connection request from {EndPoint}", remoteEP);
                    }
                }
                return;
            }
            if (targetConnection==0xBDDDDDDDD) // we got a connection response
            {
                Log.Logger.Information("Received connection response from {EndPoint}", remoteEP);
                // read connection response
                BinaryReader reader = new BinaryReader(ms);
                try
                {
                    ConnectionResponse response = Serializer.Deserialize<ConnectionResponse>(reader);
                    if (!response.Accepted)
                    {
                        Log.Logger.Warning("Connection request was rejected by {EndPoint} :(", remoteEP);
                    }
                    var connection = Connections.Find(x => x.ConnectionId == response.ConnectionId);
                    connection.SetPeerPublicKey(response.PublicKey);
                    Log.Logger.Information("Connection {ConnectionID} established with {EndPoint}",response.ConnectionId, remoteEP);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
                }
                return;
            }
            
            if (c is null)
            {
                return;
            }
            c.ReadInputStream(ms);
        }
        
        public byte[] ComputeSharedSecretKey(AsymmetricKeyParameter remotePublicKey, ulong connectionID)
        {
            byte[] sharedSecret = CryptoHelper.GenerateDHKey(_keyPair.Private, remotePublicKey);
            
            // add connection id to shared secret
            byte[] hash = new byte[32];
            SHA256Managed sha = new SHA256Managed();
            sha.TransformBlock(sharedSecret, 0, sharedSecret.Length, null, 0);
            sha.TransformFinalBlock(BitConverter.GetBytes(connectionID), 0, 8);
            
            return sha.Hash;
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