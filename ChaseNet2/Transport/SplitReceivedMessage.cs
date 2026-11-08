using System;
using System.Collections.Generic;
using ChaseNet2.Serialization;
using ChaseNet2.Transport.Messages;

namespace ChaseNet2.Transport
{
    public class SplitReceivedMessage
    {
        public ulong OriginalMessageId;
        public ulong ChannelId;
        public MessageType Type;
        public HashSet<int> ReceivedParts;
        public int TotalParts;
        public int PartSize;
        public byte[] Buffer;

        public SplitReceivedMessage(SplitMessagePart part)
        {
            OriginalMessageId = part.OriginalMessageId;
            TotalParts = part.TotalParts;
            PartSize = part.PartSize;
            ChannelId = part.Channel;
            Type = part.OriginalMessageType;
            ReceivedParts = new HashSet<int>();
            Buffer = new byte[part.TotalParts * part.PartSize];

            AddPart(part);
        }

        public void AddPart(SplitMessagePart part)
        {
            if (part.TotalParts != TotalParts || part.PartSize != PartSize || part.Data is null || part.Data.Length > PartSize ||
                part.PartNumber < 0 || part.PartNumber >= TotalParts)
            {
                throw new ArgumentException($"Split message part does not match message {OriginalMessageId}");
            }

            if (ReceivedParts.Contains(part.PartNumber))
                return;

            int offset = part.PartNumber * part.PartSize;
            Array.Copy(part.Data, 0, Buffer, offset, part.Data.Length);
            ReceivedParts.Add(part.PartNumber);
        }

        public bool IsComplete()
        {
            return ReceivedParts.Count == TotalParts;
        }

        public NetworkMessage GetCompleteMessage(SerializationManager manager)
        {
            if (!IsComplete())
                throw new Exception("Message is not complete");

            var content = manager.Deserialize(Buffer);

            return new NetworkMessage(
                OriginalMessageId,
                ChannelId,
                Type,
                content
            );
        }
    }
}
