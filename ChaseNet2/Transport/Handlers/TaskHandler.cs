using System.Threading.Tasks;
using Serilog;

namespace ChaseNet2.Transport
{
    public class TaskHandler:IMessageHandler
    {
        public TaskCompletionSource<NetworkMessage> TaskCompletionSource;

        public TaskHandler()
        {
            TaskCompletionSource = new TaskCompletionSource<NetworkMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        public void HandleMessage(Connection connection, NetworkMessage message)
        {
            TaskCompletionSource.SetResult(message);
        }
    }
}