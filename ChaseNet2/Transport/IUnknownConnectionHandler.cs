using System.IO;
using System.Net;

namespace ChaseNet2.Transport
{
    public interface IUnknownConnectionHandler
    {
        void OnMessageFromUnknownConnectionReceived(ulong connectionID, IPEndPoint endPoint, Stream dataStream);
    }
}