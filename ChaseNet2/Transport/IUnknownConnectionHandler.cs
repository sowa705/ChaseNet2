using System.IO;

namespace ChaseNet2.Transport
{
    public interface IUnknownConnectionHandler
    {
        void OnMessageFromUnknownConnectionReceived(ulong connectionID, Stream dataStream);
    }
}