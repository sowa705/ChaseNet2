using ChaseNet2.Contract;
using ChaseNet2.Relay;

namespace ChaseNet2.Relay
{
    public class RelaySetupContract : NetworkContract<RelaySetupContract.SenderState,RelaySetupContract.ReceiverState>
    {
        public enum SenderState
        {
            Start,
            RequestSent,
            Success,
            Failure
        }
        public enum ReceiverState
        {
            Start,
            ResponseSent
        }
        
        public RelaySetupContract()
        {
            AddReceiverTransition(
                ReceiverState.Start,
                typeof(RelayRequest),
                (content) => { base.ReceiverState = ReceiverState.ResponseSent; });
            
            AddSenderTransition(
                SenderState.RequestSent,
                typeof(RelayRequestResponse),
                o =>
                {
                    if (o is RelayRequestResponse { Accepted: true })
                    {
                        base.SenderState = SenderState.Success;
                    }
                    else
                    {
                        base.SenderState = SenderState.Failure;
                    }
                });
        }
    }
}