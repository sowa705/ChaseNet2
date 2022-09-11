namespace ChaseNet2.Session
{
    public enum SessionClientState
    {
        StartedConnection,
        AwaitingResponse,
        Connected,
        Disconnected,
        Rejected
    }
}