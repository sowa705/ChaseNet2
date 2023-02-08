using System;
using System.Collections.Generic;
using System.Linq;
using ChaseNet2.Transport;

namespace ChaseNet2.Contract
{
    public class NetworkContract<SenderStateT, ReceiverStateT>
    {
        private List<(SenderStateT from, Type triggerType, Action<object> onTrigger)> SenderTransitions =
            new List<(SenderStateT from, Type triggerType, Action<object> onTrigger)>();
        
        private List<(ReceiverStateT from, Type triggerType, Action<object> onTrigger)> ReceiverTransitions =
            new List<(ReceiverStateT from, Type triggerType, Action<object> onTrigger)>();

        public SenderStateT SenderState;
        public ReceiverStateT ReceiverState;
        
        public bool IsSender;

        public bool Equals = true;

        public ulong Channel { get; private set; }
        public Connection Connection { get; private set; }
        
        public void AddSenderTransition(SenderStateT from, Type triggerType, Action<object> onReceived)
        {
            SenderTransitions.Add((from,triggerType,onReceived));
        }
        public void AddReceiverTransition(ReceiverStateT from, Type triggerType, Action<object> onReceived)
        {
            ReceiverTransitions.Add((from,triggerType,onReceived));
        }

        private bool StatesEqual(SenderStateT a, SenderStateT b)
        {
            if (Equals)
            {
                return a.Equals(b);
            }

            return a.GetType() == b.GetType();
        }

        private bool StatesEqual(ReceiverStateT a, ReceiverStateT b)
        {
            if (Equals)
            {
                return a.Equals(b);
            }

            return a.GetType() == b.GetType();
        }

        public void Fire(NetworkMessage message)
        {
            if (IsSender)
            {
                FireSender(message);
            }
            else
            {
                FireReceiver(message);
            }
        }

        void FireSender(NetworkMessage msg)
        {
            var availableTransitions = SenderTransitions.Where(x => StatesEqual(x.from,SenderState));

            var transition = availableTransitions.First(x => x.triggerType == msg.ContentType); // will throw if the desired transition doensn't exist
            
            transition.onTrigger.Invoke(msg.Content);
        }
        
        void FireReceiver(NetworkMessage msg)
        {
            var availableTransitions = ReceiverTransitions.Where(x => StatesEqual(x.from,ReceiverState));

            var transition = availableTransitions.First(x => x.triggerType == msg.ContentType); // will throw if the desired transition doensn't exist
            
            transition.onTrigger.Invoke(msg.Content);
        }
    }
}