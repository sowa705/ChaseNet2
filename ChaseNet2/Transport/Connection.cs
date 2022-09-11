﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using ChaseNet2.Transport.Messages;

namespace ChaseNet2.Transport
{
    public class Connection
    {
        public ECDiffieHellmanPublicKey PeerPublicKey;
        public IPEndPoint RemoteEndpoint;
        private ConnectionManager _manager;
        
        public ConnectionState State { get; private set; }
        private byte[] _sharedKey;
        
        public ConcurrentQueue<NetworkMessage> IncomingMessages { get; private set; }
        ConcurrentQueue<NetworkMessage> OutgoingMessages { get; set; }
        
        List<NetworkMessage> _sentMessages;
        
        public uint CurrentMessageId { get; private set; }

        private Random _rng;
        
        public DateTime LastPing { get; private set; }
        public DateTime LastReceivedPong { get; private set; }
        
        public int RandomPingNumber { get; private set; }
        
        public float AveragePing { get; private set; }
        
        public DateTime LastConnectionAttempt { get; private set; }

        public Connection(ConnectionManager manager,ConnectionTarget target)
        {
            PeerPublicKey = target.PublicKey;
            RemoteEndpoint = target.EndPoint;
            
            _manager = manager;
            
            _sharedKey = manager.ComputeSharedSecretKey(PeerPublicKey);
            
            IncomingMessages = new ConcurrentQueue<NetworkMessage>();
            OutgoingMessages = new ConcurrentQueue<NetworkMessage>();
            
            _sentMessages = new List<NetworkMessage>();
            
            LastPing = DateTime.UtcNow;
            LastReceivedPong = DateTime.UtcNow;
            
            _rng=new Random();
            LastConnectionAttempt = DateTime.UtcNow;
            CreateConnectMessage();
        }

        public NetworkMessage EnqueueMessage(MessageType type, object obj)
        {
            var message = new NetworkMessage(CurrentMessageId++, type, obj);
            OutgoingMessages.Enqueue(message);
            return message;
        }
        
        public void CreateConnectMessage()
        {
            BinaryWriter writer = new BinaryWriter(new MemoryStream());
            
            writer.Write(0xADDDDDD); //connection magic number

            ConnectionRequest request = new ConnectionRequest() {PublicKey = _manager.PublicKey};
            
            _manager.Serializer.Serialize(request,writer);

            _manager.SendPacket(((MemoryStream)writer.BaseStream).ToArray(), RemoteEndpoint);
        }
        
        public void ReadInputStream(Stream stream)
        {
            var initializationVector = new byte[16];
            var ivCount = stream.Read(initializationVector, 0, 16); //Read the initialization vector for the packet AES encryption

            if (ivCount != 16)
            {
                throw new Exception("Invalid initialization vector length");
            }
            
            // decrypt the packet
            using var aes = _manager.CreateAes(_sharedKey,initializationVector);
            using var cs = new CryptoStream(stream, aes.CreateDecryptor(_sharedKey,initializationVector), CryptoStreamMode.Read);
            
            // decompress the packet
            using var ds = new DeflateStream(cs, CompressionMode.Decompress);
            
            // read packet preamble
            var reader = new BinaryReader(ds);

            try
            {
                var preamble = reader.ReadInt32();
                if (preamble != 0x12345678)
                {
                    Console.WriteLine("Invalid packet preamble");
                    return;
                }
            }
            catch
            {
                Console.WriteLine("Invalid packet");
                return;
            }
            
            try
            {
                // read messages (packet is a list of messages)
                while (true)
                {
                    var messageID = reader.ReadUInt32();
                    var messageType = (MessageType) reader.ReadByte();
                    var messageContent = _manager.Serializer.Deserialize(reader);
                    
                    var message = new NetworkMessage(messageID, messageType, messageContent);
                    message.State = MessageState.Received;
                    
                    //Console.WriteLine("Message received: " + message);
                    
                    if (messageType.HasFlag(MessageType.Reliable)) //we need to acknowledge this message
                    {
                        EnqueueMessage(MessageType.Internal, new Ack() { MessageID = messageID });
                    }

                    if (messageType.HasFlag(MessageType.Internal))
                    {
                        HandleInternalMessage(message);
                        continue;
                    }
                    
                    IncomingMessages.Enqueue(message);
                }
            }
            catch(EndOfStreamException e)
            {
            }
        }

        private void HandleInternalMessage(NetworkMessage message)
        {
            switch (message.Content)
            {
                case Ack ack:
                    var sentMessage = _sentMessages.Find(m => m.ID == ack.MessageID);
                    if (sentMessage != null)
                    {
                        sentMessage.State = MessageState.Delivered;
                    }
                    break;
                case Ping ping:
                    Pong p = new Pong();
                    p.RandomNumber = ping.RandomNumber;
                    EnqueueMessage(MessageType.Internal, p);
                    break;
                case Pong pong:
                    if (pong.RandomNumber == RandomPingNumber)
                    {
                        // we got a valid pong
                        var pingTime = (DateTime.UtcNow - LastPing)/2; // ping is half of round trip time
                        
                        AveragePing = (AveragePing + (float) pingTime.TotalMilliseconds) / 2;
                        LastReceivedPong = DateTime.UtcNow;
                        
                        //Console.WriteLine($"Ping {AveragePing}");
                        
                        State = ConnectionState.Connected; // ping came back so obviously we are connected
                    }
                    break;
                default:
                    Console.WriteLine($"Unknown internal message type: {message.Content}");
                    break;
            }
        }

        public void Update()
        {
            if (State == ConnectionState.Started&& LastConnectionAttempt.AddSeconds(3) < DateTime.UtcNow)
            {
                LastConnectionAttempt = DateTime.UtcNow;
                CreateConnectMessage();
                Console.WriteLine("Retrying connection");
            }
            if (LastPing+TimeSpan.FromSeconds(2)<DateTime.UtcNow)
            {
                LastPing = DateTime.UtcNow;
                RandomPingNumber = _rng.Next();
                Ping p = new Ping() { RandomNumber = RandomPingNumber };
                EnqueueMessage(MessageType.Internal, p);
            }

            if (LastReceivedPong+TimeSpan.FromSeconds(5)<DateTime.UtcNow&&State!=ConnectionState.Started)
            {
                //Console.WriteLine("Disconnecting due to timeout");
                State = ConnectionState.Disconnected; // we haven't received a pong in 5 seconds so we are probably disconnected
            }
            
            // check for messages that need to be resent

            foreach (var msg in _sentMessages)
            {
                if (msg.State==MessageState.Sent && msg.LastSent+GetResendInterval()<DateTime.UtcNow)
                {
                    if (msg.ResendCount>3) //failed to deliver message
                    {
                        msg.State = MessageState.Failed;
                        continue;
                    }
                    // resend the message
                    msg.LastSent=DateTime.UtcNow;
                    msg.ResendCount++;
                    OutgoingMessages.Enqueue(msg);
                }
            }
            
            // remove messages that have been delivered or failed
            
            _sentMessages.RemoveAll(m => m.State == MessageState.Delivered || m.State == MessageState.Failed);
            
            // send messages
            SendMessages();
        }
        /// <summary>
        /// compute the resend interval based on the average ping and reasonable lower and upper bounds
        /// </summary>
        /// <returns></returns>
        public TimeSpan GetResendInterval()
        {
            var resendInterval = (int) (AveragePing * 3.5f); //rtt + some extra time
            
            resendInterval = Math.Clamp(resendInterval, 100, 600); // clamp to reasonable values
            
            return TimeSpan.FromMilliseconds(resendInterval);
        }

        private void SendMessages()
        {
            while (!OutgoingMessages.IsEmpty)
            {
                // prepare the packet
                var iv = _manager.GenerateInitializationVector();
                
                var ms = new MemoryStream();
                // write the initialization vector for the packet AES encryption
                ms.Write(iv);

                // prepare encryption and compression streams
                var aes = _manager.CreateAes(_sharedKey, iv);
                var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write);
                var ds = new DeflateStream(cs, CompressionLevel.Optimal, true);
                // write packet preamble
                var writer = new BinaryWriter(ds);
                writer.Write(0x12345678);
                
                // write messages until we run out of space or packets
                var maxPacketSize = 32768; //intentionally small to account for potential overhead
                
                var currentPacketSize = 16+4; //initialization vector + preamble
                while (!OutgoingMessages.IsEmpty && currentPacketSize < maxPacketSize)
                {
                    OutgoingMessages.TryDequeue(out var message);
                    writer.Write(message.ID);
                    writer.Write((byte) message.Type);
                    int contentSize=_manager.Serializer.Serialize(message.Content,writer);
                    
                    message.State = MessageState.Sent;
                    if (message.Type==MessageType.Reliable) //we need to track this message for later
                    {
                        _sentMessages.Add(message);
                    }

                    currentPacketSize += 4 + 1 + contentSize; //messageID + messageType + content
                }
                
                // flush the streams
                writer.Flush();
                ds.Flush();
                cs.FlushFinalBlock();
                ms.Flush();
                // send the packet
                _manager.SendPacket(ms.ToArray(), RemoteEndpoint);
            }
        }
    }
}