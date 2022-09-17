using System.Threading.Tasks;

namespace ChaseNet2.Transport
{
    public class TaskHandler:IMessageHandler
    {
        public TaskCompletionSource<bool> TaskCompletionSource;
        public NetworkMessage Message;

        public TaskHandler()
        {
            TaskCompletionSource = new TaskCompletionSource<bool>();
        }
        public void HandleMessage(Connection connection, NetworkMessage message)
        {
            Message = message;
            TaskCompletionSource.SetResult(true);
        }
    }
}