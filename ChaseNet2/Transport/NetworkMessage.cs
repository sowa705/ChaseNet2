using System;

namespace ChaseNet2
{
    public class NetworkMessage
    {
        public uint ID { get; set; }
        public MessageType Type { get; set; }
        public object Content { get; set; }
        public MessageState State { get; set; }
        
        public DateTime LastSent { get; set; }
        public int ResendCount { get; set; }
        
        public NetworkMessage(uint id, MessageType type, object content)
        {
            ID = id;
            Type = type;
            Content = content;
        }

        public override string ToString()
        {
            return $"ID: {ID}, Type: {Type}, Data: {Content}, State: {State}, LastSent: {LastSent}, ResendCount: {ResendCount}";
        }
    }
}