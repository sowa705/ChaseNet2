using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ChaseNet2.Transport
{
    public class SentMessage
    {
        public NetworkMessage Message { get; set; }
        public DateTime LastSent { get; set; }
        public int ResendCount { get; set; }
        public bool IsSplit { get; set; }
        public List<NetworkMessage> SentFragmentMessages { get; set; }
        
        public TaskCompletionSource<bool>? DeliveryTask { get; set; }

        public SentMessage(NetworkMessage message)
        {
            Message = message;
            ResendCount = 0;
            LastSent = DateTime.UtcNow;
            SentFragmentMessages = new List<NetworkMessage>();
        }
    }
}