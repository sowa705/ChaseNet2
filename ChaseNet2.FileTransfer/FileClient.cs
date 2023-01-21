using ChaseNet2.Transport;
using Newtonsoft.Json;
using Serilog;

namespace ChaseNet2.FileTransfer;

public class FileClient : ConnectionHandler
{
    public List<(Connection, FileSpec)> DiscoveredFiles = new List<(Connection, FileSpec)>();

    FileTransfer? CurrentTransfer;

    NetworkMessage? SentRequest;

    public override Task OnAttached(ConnectionManager manager)
    {
        return Task.CompletedTask;
    }

    public override Task OnManagerConnect(Connection connection)
    {
        AddConnection(connection.ConnectionId);
        return Task.CompletedTask;
    }

    public override void ConnectionUpdate(Connection connection)
    {
        while (connection.IncomingMessages.TryDequeue(out var message))
        {
            switch (message.Content)
            {
                case FileSpec fileSpec:
                    Log.Information("Received file spec from {Connection}", connection.ConnectionId);
                    if (DiscoveredFiles.All(x => x.Item2.FileName != fileSpec.FileName))
                    {
                        DiscoveredFiles.Add((connection, fileSpec));
                    }
                    break;
                case FilePartResponse filePartResponse:
                    HandleFilePartResponse(filePartResponse);
                    break;
            }
        }
    }

    private void HandleFilePartResponse(FilePartResponse filePartResponse)
    {
        CurrentTransfer.DestinationStream.Seek(0, SeekOrigin.Begin);
        CurrentTransfer.DestinationStream.Write(filePartResponse.Data, (int)filePartResponse.Offset, filePartResponse.Data.Length);
        CurrentTransfer.Progress.DownloadedParts.Add(filePartResponse.Offset);

        Log.Information("Downloaded {0} of {1} parts", CurrentTransfer.Progress.DownloadedParts.Count, CurrentTransfer.FileSpec.Parts.Count);

        if (CurrentTransfer.Progress.DownloadedParts.Count == CurrentTransfer.FileSpec.Parts.Count)
        {
            Log.Information("File transfer complete");
            CurrentTransfer.DestinationStream.Close();
            CurrentTransfer.DestinationStream.Dispose();
            CurrentTransfer = null;
        }

        SentRequest = null;

        // save progress
        var SerializedProgress = JsonConvert.SerializeObject(CurrentTransfer.Progress);
        File.WriteAllText(CurrentTransfer.FileSpec.FileName + ".progress", SerializedProgress);
    }

    public void StartTransfer(FileSpec fileSpec, string destinationPath)
    {
        Log.Information("Starting transfer of {0}", fileSpec.FileName);
        var connection = DiscoveredFiles.First(x => x.Item2.FileName == fileSpec.FileName).Item1;

        CurrentTransfer = new FileTransfer
        {
            Source = connection,
            DestinationPath = destinationPath,
            FileSpec = fileSpec,
            DestinationStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write)
        };

        // Check if there is some progress already
        var progressFile = new FileInfo(destinationPath + ".progress");
        if (progressFile.Exists)
        {
            var progress = JsonConvert.DeserializeObject<FileTransferProgress>(File.ReadAllText(progressFile.FullName));
            CurrentTransfer.Progress = progress;
        }
    }

    public override void Update()
    {
        if (CurrentTransfer == null) return;

        if (SentRequest == null)
        {
            long offset = 0;

            // find the next part to download
            foreach (var part in CurrentTransfer.FileSpec.Parts)
            {
                if (!CurrentTransfer.Progress.DownloadedParts.Contains(part.Offset))
                {
                    offset = part.Offset;
                    break;
                }
            }

            var filePartRequest = new FilePartRequest
            {
                FileName = CurrentTransfer.FileSpec.FileName,
                Offset = offset
            };
            Log.Information("Requesting part {0}", offset);
            SentRequest = CurrentTransfer.Source.EnqueueMessage(MessageType.Priority | MessageType.Reliable, 997, filePartRequest);
        }
        else
        {
            if (SentRequest.State == MessageState.Failed)
            {
                Log.Error("Failed to send request, retrying...");
                SentRequest = null;
            }
        }
    }
}