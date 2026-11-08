using System.Threading.Tasks;

namespace ChaseNet2.Transport
{
    public class NetworkMessage
    {
        private volatile MessageState _state;

        public ulong ID { get; set; }
        public ulong ChannelID { get; set; }
        public MessageType Type { get; set; }
        public object Content { get; set; }

        /// <summary>
        /// Delivery state. Updated by the network thread; safe to read from any thread.
        /// </summary>
        public MessageState State
        {
            get => _state;
            set => _state = value;
        }

        /// <summary>
        /// Wire bytes of <see cref="Content"/>, captured when an outgoing message is enqueued.
        /// Later changes to the content object are not sent; resends reuse these bytes.
        /// </summary>
        internal byte[]? SerializedContent { get; set; }

        /// <summary>
        /// Completed with true when a reliable message is acknowledged, false when it fails. Created at enqueue time.
        /// </summary>
        internal TaskCompletionSource<bool>? DeliveryCompletion { get; set; }

        public NetworkMessage(ulong id, ulong channelId, MessageType type, object content)
        {
            ID = id;
            ChannelID = channelId;
            Type = type;
            Content = content;
        }

        internal void MarkDelivered()
        {
            State = MessageState.Delivered;
            DeliveryCompletion?.TrySetResult(true);
        }

        internal void MarkFailed()
        {
            State = MessageState.Failed;
            DeliveryCompletion?.TrySetResult(false);
        }

        public override string ToString()
        {
            return $"ID: {ID}, Type: {Type}, Data: {Content}, State: {State}";
        }

        public T As<T>() where T : class
        {
            if (Content is T content)
            {
                return content;
            }
            throw new InvalidNetworkMessageTypeException($"Cannot convert message content of type {Content.GetType()} to type {typeof(T)}");
        }
    }
}
