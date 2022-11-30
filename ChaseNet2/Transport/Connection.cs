using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ChaseNet2.Transport.Messages;
using Serilog;
using Serilog.Core;

namespace ChaseNet2.Transport
{
    public partial class Connection
    {
        public ECDiffieHellmanPublicKey PeerPublicKey;
        public ulong ConnectionId { get; private set; }
        
        public IPEndPoint RemoteEndpoint;
        private ConnectionManager _manager;
        
        public ConnectionState State { get; private set; }
        private byte[] _sharedKey;
        
        public ConcurrentQueue<NetworkMessage> IncomingMessages { get; private set; }
        ConcurrentQueue<NetworkMessage> OutgoingMessages { get; set; }
        
        List<SentMessage> _trackedSentMessages;
        LinkedList<ulong> _ReceivedMessageIds;
        
        
        public ulong CurrentMessageId { get; private set; }

        private Random _rng;
        
        public DateTime LastPing { get; private set; }
        public DateTime LastReceivedPong { get; private set; }
        
        public int RandomPingNumber { get; private set; }
        
        public float AveragePing { get; private set; }
        
        public DateTime LastConnectionAttempt { get; private set; }
        int ConnectionRetryCount;

        public Dictionary<ulong,IMessageHandler> MessageHandlers { get; private set; }

        public Connection(ConnectionManager manager,ConnectionTarget target)
        {
            PeerPublicKey = target.PublicKey;
            RemoteEndpoint = target.EndPoint;
            ConnectionId = target.ConnectionId;
            
            _manager = manager;

            if (PeerPublicKey!=null)
            {
                SetPeerPublicKey(PeerPublicKey);
            }
            
            IncomingMessages = new ConcurrentQueue<NetworkMessage>();
            OutgoingMessages = new ConcurrentQueue<NetworkMessage>();
            MessageHandlers = new Dictionary<ulong, IMessageHandler>();
            
            _trackedSentMessages = new List<SentMessage>();
            _ReceivedMessageIds = new LinkedList<ulong>();
            
            LastPing = DateTime.UtcNow;
            LastReceivedPong = DateTime.UtcNow;
            
            _rng=new Random();
            LastConnectionAttempt = DateTime.UtcNow;
            
            RegisterMessageHandler((ulong) InternalChannelType.ConnectionInternal,new InternalMessageHandler());
            
            CreateConnectMessage();
        }

        NetworkMessage EnqueueInternalMessage(MessageType type, object obj)
        {
            return EnqueueMessage(type, (ulong) InternalChannelType.ConnectionInternal, obj);
        }

        public NetworkMessage EnqueueMessage(MessageType type,ulong channelID, object obj)
        {
            var message = new NetworkMessage(CurrentMessageId++, channelID, type, obj);
            OutgoingMessages.Enqueue(message);
            
            if (message.Type==MessageType.Reliable) //we need to track this message for later
            {
                _trackedSentMessages.Add(new SentMessage(message));
            }
            
            return message;
        }
        
        public void RegisterMessageHandler(ulong channelID,IMessageHandler handler)
        {
            MessageHandlers.Add(channelID,handler);
        }
        
        public void UnregisterMessageHandler(ulong channelID)
        {
            MessageHandlers.Remove(channelID);
        }

        public async Task<bool> WaitForDeliveryAsync(NetworkMessage message)
        {
            // get the message from the list of tracked messages
            var sentMessage = _trackedSentMessages.First(x=>x.Message.ID==message.ID);

            // wait for the message to be delivered
            sentMessage.DeliveryTask=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var result = await sentMessage.DeliveryTask.Task;
            Log.Debug("Message {0} delivered",message.ID);
            return result;
        }
        
        /// <summary>
        /// Asynchronously wait for a message to arrive on a specific channel
        /// </summary>
        public async Task<NetworkMessage> WaitForChannelMessageAsync(ulong channelID, TimeSpan timeout)
        {
            // we create a handler for this channel and wait for a message to arrive
            Log.Information("Waiting for channel message on channel {channelID}",channelID);

            var handler = new TaskHandler();
            RegisterMessageHandler(channelID,handler);
            
            var task = handler.TaskCompletionSource.Task;
            await task;
            UnregisterMessageHandler(channelID);
            return task.Result;
        }
        
        public void CreateConnectMessage()
        {
            BinaryWriter writer = new BinaryWriter(new MemoryStream());

            ConnectionRequest request = new ConnectionRequest() {PublicKey = _manager.PublicKey, ConnectionId = ConnectionId};
            
            _manager.Serializer.Serialize(request,writer);

            _manager.SendPacket(((MemoryStream)writer.BaseStream).ToArray(), RemoteEndpoint, 0xADDDDDDDD);
        }
        
        public void SendConnectionResponse()
        {
            BinaryWriter writer = new BinaryWriter(new MemoryStream());

            ConnectionResponse request = new ConnectionResponse() {PublicKey = _manager.PublicKey, Accepted = true, ConnectionId = ConnectionId};
            
            _manager.Serializer.Serialize(request,writer);

            _manager.SendPacket(((MemoryStream)writer.BaseStream).ToArray(), RemoteEndpoint, 0xBDDDDDDDD);
        }
        
        public void SetPeerPublicKey(ECDiffieHellmanPublicKey key)
        {
            PeerPublicKey = key;
            _sharedKey = _manager.ComputeSharedSecretKey(PeerPublicKey,ConnectionId);
        }
        
        public void ReadInputStream(Stream stream)
        {
            if (PeerPublicKey==null)
            {
                return;
            }
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
                    Log.Logger.Warning("Received an invalid packet preamble");
                    return;
                }
            }
            catch
            {
                Log.Logger.Warning("Received an invalid packet");
                return;
            }
            
            try
            {
                // read messages (packet is a list of messages)
                while (true)
                {
                    var messageID = reader.ReadUInt64();
                    var channelID = reader.ReadUInt64();
                    var messageType = (MessageType) reader.ReadByte();
                    var messageContent = _manager.Serializer.Deserialize(reader);
                    
                    var message = new NetworkMessage(messageID, channelID, messageType, messageContent);
                    message.State = MessageState.Received;

                    if (_ReceivedMessageIds.Contains(messageID))
                    {
                        Log.Logger.Warning("Received a duplicate message {messageID}",messageID);
                        continue;
                    }
                    
                    _ReceivedMessageIds.AddLast(messageID);
                    
                    Log.Logger.Debug("Received message {MessageID} on channel {ChannelID} of type {MessageType} with content {Content}",messageID,channelID,messageType,messageContent.GetType());
                    
                    if (messageType.HasFlag(MessageType.Reliable)) //we need to acknowledge this message
                    {
                        EnqueueInternalMessage(MessageType.Unreliable, new Ack() { MessageID = messageID });
                    }

                    if (MessageHandlers.ContainsKey(channelID))
                    {
                        MessageHandlers[channelID].HandleMessage(this, message);
                    }
                    else
                    {
                        IncomingMessages.Enqueue(message);
                    }
                }
            }
            catch(EndOfStreamException e)
            {
            }
        }

        public void Update()
        {
            if (State == ConnectionState.Started&& LastConnectionAttempt.AddSeconds(3) < DateTime.UtcNow)
            {
                LastConnectionAttempt = DateTime.UtcNow;
                ConnectionRetryCount++;
                if (ConnectionRetryCount > 5)
                {
                    State = ConnectionState.Disabled;
                    Log.Logger.Error("Failed to connect to peer");
                    return;
                }
                CreateConnectMessage();
                Log.Logger.Warning("Retrying sending connection request to {0}", RemoteEndpoint);
            }
            if (LastPing+TimeSpan.FromSeconds(2)<DateTime.UtcNow)
            {
                LastPing = DateTime.UtcNow;
                RandomPingNumber = _rng.Next();
                Ping p = new Ping() { RandomNumber = RandomPingNumber };
                EnqueueInternalMessage(MessageType.Unreliable, p);
            }

            if (LastReceivedPong+TimeSpan.FromSeconds(5)<DateTime.UtcNow&&State!=ConnectionState.Started)
            {
                //Log.Logger.Warning("Lost connection {0}, trying to reconnect", ConnectionId);
                State = ConnectionState.Disconnected; // we haven't received a pong in 5 seconds so we are probably disconnected
            }
            
            // check for messages that need to be resent

            foreach (var msg in _trackedSentMessages)
            {
                if (msg.Message.State==MessageState.Sent && DateTime.UtcNow>msg.LastSent+GetResendInterval())
                {
                    if (msg.ResendCount>3) //failed to deliver message
                    {
                        msg.Message.State = MessageState.Failed;
                        Log.Information("Failed to deliver message {MessageID}",msg.Message.ID);
                        msg.DeliveryTask?.SetResult(false);
                        continue;
                    }
                    Log.Warning("Resending message {MessageID}",msg.Message.ID);
                    // resend the message
                    msg.LastSent=DateTime.UtcNow;
                    msg.ResendCount++;
                    OutgoingMessages.Enqueue(msg.Message);
                }
            }
            
            // clean up old message ids
            while (_ReceivedMessageIds.Count>200)
            {
                _ReceivedMessageIds.RemoveFirst();
            }
            
            // remove messages that have been delivered or failed
            
            _trackedSentMessages.RemoveAll(m => m.Message.State == MessageState.Delivered || m.Message.State == MessageState.Failed);
            
            // send messages
            SendMessages();
        }
        
        /// <summary>
        /// compute the resend interval based on the average ping and reasonable lower and upper bounds
        /// </summary>
        /// <returns></returns>
        public TimeSpan GetResendInterval()
        {
            var resendInterval = (int) (AveragePing * 3f); //rtt + some extra time
            
            resendInterval = Math.Clamp(resendInterval, 80, 500); // clamp to reasonable values
            
            return TimeSpan.FromMilliseconds(resendInterval);
        }

        private void SendMessages()
        {
            while (!OutgoingMessages.IsEmpty)
            {
                if (PeerPublicKey==null)
                {
                    return;
                }
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
                    writer.Write(message.ChannelID);
                    writer.Write((byte) message.Type);
                    int contentSize=_manager.Serializer.Serialize(message.Content,writer);
                    
                    message.State = MessageState.Sent;

                    currentPacketSize += 4 + 1 + contentSize; //messageID + messageType + content
                }
                
                // flush the streams
                writer.Flush();
                ds.Flush();
                cs.FlushFinalBlock();
                ms.Flush();
                // send the packet
                _manager.SendPacket(ms.ToArray(), RemoteEndpoint, ConnectionId);
            }
        }
    }
}