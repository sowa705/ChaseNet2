using System.Threading.Tasks;

namespace ChaseNet2.Transport
{
    /// <summary>
    /// Completes a task with the first message received on its channel. Thread-safe, so it is registered
    /// with <see cref="MessageDispatchMode.Inline"/>.
    /// </summary>
    public class TaskHandler : IMessageHandler
    {
        public TaskCompletionSource<NetworkMessage> TaskCompletionSource;

        public TaskHandler()
        {
            TaskCompletionSource = new TaskCompletionSource<NetworkMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void HandleMessage(Connection connection, NetworkMessage message)
        {
            TaskCompletionSource.TrySetResult(message);
        }
    }
}
