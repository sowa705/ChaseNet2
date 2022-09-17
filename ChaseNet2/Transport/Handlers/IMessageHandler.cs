namespace ChaseNet2.Transport
{
    public interface IMessageHandler
    {
        void HandleMessage(Connection connection, NetworkMessage message);
    }
}