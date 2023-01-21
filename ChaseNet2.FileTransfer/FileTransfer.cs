using ChaseNet2.Transport;

namespace ChaseNet2.FileTransfer;

public class FileTransfer
{
    public Connection Source;
    public FileSpec FileSpec;
    public string DestinationPath;
    public FileStream DestinationStream;
    public FileTransferProgress Progress;
}

public class FileTransferProgress
{
    public List<long> DownloadedParts=new List<long>();
}