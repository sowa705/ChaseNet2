namespace ChaseNet2.Transport
{
    public enum MessageState
    {
        Created,
        Sent,
        Delivered,
        Received,
        WaitingForParts,
        Failed
    }
}