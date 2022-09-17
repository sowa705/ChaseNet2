using System;
using System.Threading.Tasks;

namespace ChaseNet2.Transport
{
    public class NetworkMessage
    {
        public ulong ID { get; set; }
        public ulong ChannelID { get; set; }
        public MessageType Type { get; set; }
        public object Content { get; set; }
        public MessageState State { get; set; }

        
        public NetworkMessage(ulong id, ulong channelId, MessageType type, object content)
        {
            ID = id;
            ChannelID = channelId;
            Type = type;
            Content = content;
        }

        public override string ToString()
        {
            return $"ID: {ID}, Type: {Type}, Data: {Content}, State: {State}";
        }
    }
}