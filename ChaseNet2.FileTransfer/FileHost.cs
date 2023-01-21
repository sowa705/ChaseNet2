using System.Text.Json;
using ChaseNet2.Transport;
using Serilog;

public class FileHost : ConnectionHandler, IMessageHandler
{
    FileSpec Spec;
    private string FilePath;
    FileStream Stream;

    List<Connection> Connections = new List<Connection>();
    DateTime LastBroadcastTime;

    public FileHost(string filePath)
    {
        FilePath = filePath;
        LastBroadcastTime = DateTime.UtcNow;
    }

    public override async Task OnAttached(ConnectionManager manager)
    {
        Log.Information("Starting file host");
        Spec = await FileSpec.Create(FilePath);
        Stream = new FileStream(FilePath, FileMode.Open);

        manager.Serializer.RegisterType(typeof(FilePartRequest));
        manager.Serializer.RegisterType(typeof(FilePartResponse));
    }

    public override Task OnManagerConnect(Connection connection)
    {
        connection.RegisterMessageHandler(997, this);
        Connections.Add(connection);

        return Task.CompletedTask;
    }

    public override void ConnectionUpdate(Connection connection)
    {
    }

    public override void Update()
    {
        if (LastBroadcastTime.AddSeconds(2) < DateTime.UtcNow)
        {
            LastBroadcastTime = DateTime.UtcNow;
            Log.Information("Broadcasting file spec");

            foreach (var connection in Connections)
            {
                connection.EnqueueMessage(MessageType.Reliable | MessageType.Priority, 997, Spec);
            }
        }
    }

    public void HandleMessage(Connection connection, NetworkMessage message)
    {
        switch (message.Content)
        {
            case FilePartRequest request:
                if (request.FileName != Spec.FileName)
                {
                    Log.Warning("Client requested file {0} but we are hosting {1}", request.FileName, Spec.FileName);
                    return;
                }
                var part = new FilePartResponse();
                part.FileName = Spec.FileName;
                part.Offset = request.Offset;

                var buffer = new byte[request.Length];

                Stream.Seek(request.Offset, SeekOrigin.Begin);
                Stream.Read(buffer, 0, request.Length);

                part.Data = buffer;

                connection.EnqueueMessage(MessageType.Reliable, 997, part);
                Log.Information("Sent {0} bytes at offset {1} of file {2} to client", request.Length, request.Offset, Spec.FileName);
                break;
        }
    }
}