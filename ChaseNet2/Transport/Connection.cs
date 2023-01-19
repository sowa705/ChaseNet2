using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ChaseNet2.Extensions;
using ChaseNet2.Transport.Messages;
using Org.BouncyCastle.Crypto;
using Serilog;
using Serilog.Core;

namespace ChaseNet2.Transport
{
    public partial class Connection
    {
        public AsymmetricKeyParameter PeerPublicKey;
        public ulong ConnectionId { get; private set; }
        
        public IPEndPoint RemoteEndpoint;
        private ConnectionManager _manager;
        
        public ConnectionState State { get; private set; }
        private byte[] _sharedKey;
        
        public ConcurrentQueue<NetworkMessage> IncomingMessages { get; private set; }
        ConcurrentQueue<NetworkMessage> OutgoingMessages { get; set; }
        
        Dictionary<ulong,SentMessage> _trackedSentMessages;
        // Messages that are waiting for other parts to arrive
        Dictionary<ulong,SplitReceivedMessage> _splitReceivedMessages;
        
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
            
            _trackedSentMessages = new Dictionary<ulong,SentMessage>();
            _splitReceivedMessages = new Dictionary<ulong, SplitReceivedMessage>();
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
                _trackedSentMessages.Add(message.ID,new SentMessage(message));
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
            var sentMessage = _trackedSentMessages[message.ID];

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
            
            writer.Write(_manager.Serializer.Serialize(request));

            _manager.SendPacket(((MemoryStream)writer.BaseStream).ToArray(), RemoteEndpoint, 0xADDDDDDDD);
        }
        
        public void SendConnectionResponse()
        {
            BinaryWriter writer = new BinaryWriter(new MemoryStream());

            ConnectionResponse request = new ConnectionResponse() {PublicKey = _manager.PublicKey, Accepted = true, ConnectionId = ConnectionId};
            
            writer.Write(_manager.Serializer.Serialize(request));

            _manager.SendPacket(((MemoryStream)writer.BaseStream).ToArray(), RemoteEndpoint, 0xBDDDDDDDD);
        }
        
        public void SetPeerPublicKey(AsymmetricKeyParameter key)
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
            //using var ds = new DeflateStream(cs, CompressionMode.Decompress);
            
            // read packet preamble
            var reader = new BinaryReader(cs);

            try
            {
                var preamble = reader.ReadInt32();
                if (preamble != 0x12345678)
                {
                    Log.Logger.Warning("Received an invalid packet preamble");
                    return;
                }
            }
            catch (Exception e)
            {
                Log.Logger.Warning("Received an invalid packet ({e})",e);
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
                    object messageContent;
                    try
                    {
                        messageContent = _manager.Serializer.Deserialize(reader);
                    }
                    catch (Exception e)
                    {
                        Log.Logger.Warning("Failed to deserialize message {messageID} ({e})",messageID,e);
                        break;
                    }
                    
                    
                    var message = new NetworkMessage(messageID, channelID, messageType, messageContent);
                    RouteReceivedMessage(message);
                }
            }
            catch(EndOfStreamException e)
            {
            }
        }

        private void RouteReceivedMessage(NetworkMessage message)
        {
            message.State = MessageState.Received;

            if (_ReceivedMessageIds.Contains(message.ID))
            {
                Log.Logger.Warning("Received a duplicate message {messageID}", message.ID);
                return;
            }

            _ReceivedMessageIds.AddLast(message.ID);

            Log.Logger.Debug("Received message {MessageID} on channel {ChannelID} of type {MessageType} with content {Content}",
                message.ID, message.ChannelID, message.Type, message.Content.GetType());

            if (message.Type.HasFlag(MessageType.Reliable)) //we need to acknowledge this message
            {
                EnqueueInternalMessage(MessageType.Unreliable, new Ack() { MessageID = message.ID });
            }

            if (MessageHandlers.ContainsKey(message.ChannelID))
            {
                MessageHandlers[message.ChannelID].HandleMessage(this, message);
            }
            else
            {
                IncomingMessages.Enqueue(message);
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

            if (LastReceivedPong+TimeSpan.FromSeconds(5)<DateTime.UtcNow&&(State!=ConnectionState.Started||State!=ConnectionState.Disconnected))
            {
                Log.Logger.Warning("Lost connection {0}, trying to reconnect", ConnectionId);
                State = ConnectionState.Disconnected; // we haven't received a pong in 5 seconds so we are probably disconnected
            }
            
            // check for messages that need to be resent

            foreach (var msg in _trackedSentMessages)
            {
                if (msg.Value.Message.State==MessageState.Sent && DateTime.UtcNow>msg.Value.LastSent+GetResendInterval() && !msg.Value.IsSplit)
                {
                    if (msg.Value.ResendCount>3) //failed to deliver message
                    {
                        msg.Value.Message.State = MessageState.Failed;
                        Log.Information("Failed to deliver message {MessageID}",msg.Value.Message.ID);
                        msg.Value.DeliveryTask?.SetResult(false);
                        continue;
                    }
                    Log.Warning("Resending message {MessageID}",msg.Value.Message.ID);
                    // resend the message
                    msg.Value.LastSent=DateTime.UtcNow;
                    msg.Value.ResendCount++;
                    OutgoingMessages.Enqueue(msg.Value.Message);
                }

                if (msg.Value.IsSplit && msg.Value.Message.State == MessageState.Sent)
                {
                    // failed to deliver a split message
                    if (DateTime.UtcNow>msg.Value.LastSent+(GetResendInterval()*8) && msg.Value.SentFragmentMessages.Any(x => x.State!=MessageState.Delivered))
                    {
                        msg.Value.Message.State = MessageState.Failed;
                        Log.Information("Failed to deliver split message {MessageID}",msg.Value.Message.ID);
                        msg.Value.DeliveryTask?.SetResult(false);
                        continue;
                    }
                }
            }
            
            // clean up old message ids
            while (_ReceivedMessageIds.Count>300)
            {
                _ReceivedMessageIds.RemoveFirst();
            }
            
            // remove messages that have been delivered or failed
            
            _trackedSentMessages.RemoveAll((k,v) => v.Message.State == MessageState.Delivered || v.Message.State == MessageState.Failed);
            
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
            
            resendInterval = Math.Clamp(resendInterval, 120, 500); // clamp to reasonable values
            
            return TimeSpan.FromMilliseconds(resendInterval);
        }

        private void SendMessages()
        {
            int totalBytesSent = 0;
            while (!OutgoingMessages.IsEmpty&&totalBytesSent<_manager.Settings.MaxBytesSentPerUpdate)
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
                //var ds = new DeflateStream(cs, CompressionLevel.Optimal, true);
                // write packet preamble
                var writer = new BinaryWriter(cs);
                writer.Write(0x12345678);
                
                // write messages until we run out of space or packets
                var maxPacketSize = 60000; //intentionally small to account for potential overhead
                
                int packetCount = 0;
                
                var currentPacketSize = 16+4; //initialization vector + preamble
                while (!OutgoingMessages.IsEmpty && currentPacketSize < maxPacketSize)
                {
                    OutgoingMessages.TryDequeue(out var message);

                    if (message.State == MessageState.Delivered || message.State == MessageState.Failed)
                    {
                        continue;
                    }
                    
                    var serializedMessage = _manager.Serializer.Serialize(message.Content);
                    int contentSize = serializedMessage.Length;
                    
                    if (contentSize>48000)
                    {
                        if (contentSize>_manager.Settings.MaxMessageLength)
                        {
                            Log.Logger.Error("Message size {Size} above the max setting {MaxSize}", contentSize, _manager.Settings.MaxMessageLength);
                            message.State = MessageState.Failed;
                            continue;
                        }
                        Log.Logger.Information("Message {MessageID} is Larger than the MTU. Splitting...", message.ID);
                        
                        SplitMessage(message, serializedMessage);
                        break;
                    }
                    
                    if (contentSize+currentPacketSize>maxPacketSize)
                    {
                        OutgoingMessages.Enqueue(message);
                        break;
                    }
                    
                    writer.Write(message.ID);
                    writer.Write(message.ChannelID);
                    writer.Write((byte) message.Type);
                    writer.Write(serializedMessage);
                    packetCount++;
                    
                    message.State = MessageState.Sent;
                    
                    totalBytesSent += contentSize+16+4+4+1; // message id + channel id + message type + content size

                    if (message.Type.HasFlag(MessageType.Reliable))
                    {
                        _trackedSentMessages[message.ID].LastSent = DateTime.UtcNow;
                    }

                    Log.Debug("Sending message {MessageID} on channel {ChannelID} of type {MessageType} with content {Content}",message.ID,message.ChannelID,message.Type,message.Content.GetType());

                    currentPacketSize += 4 + 1 + contentSize; //messageID + messageType + content
                }
                
                // flush the streams
                writer.Flush();
                //ds.Flush();
                cs.FlushFinalBlock();
                ms.Flush();
                
                Log.Debug("Sending packet with {PacketCount} messages with size of {Size}",packetCount,ms.Length);
                
                // send the packet
                _manager.SendPacket(ms.ToArray(), RemoteEndpoint, ConnectionId);
            }
        }

        private void SplitMessage(NetworkMessage message, byte[] serializedMessage)
        {
            int contentSize = serializedMessage.Length;
            _trackedSentMessages[message.ID].IsSplit = true;

            for (int i = 0; i < contentSize; i += 32768)
            {
                SplitMessagePart part = new SplitMessagePart()
                {
                    OriginalMessageId = message.ID,
                    OriginalMessageType = message.Type,
                    TotalParts = (contentSize / 32768) + 1,
                    PartNumber = i / 32768,
                    PartSize = 32768,
                    Data = serializedMessage.Skip(i).Take(32768).ToArray()
                };

                var enqueuedMsg = EnqueueInternalMessage(message.Type, part);

                if (message.Type.HasFlag(MessageType.Reliable))
                {
                    _trackedSentMessages[message.ID].SentFragmentMessages.Add(enqueuedMsg);
                }
            }
        }

        public void SetState(ConnectionState state)
        {
            State = state;
        }
    }
}