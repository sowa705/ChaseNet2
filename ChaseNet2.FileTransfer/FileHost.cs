using System.Text.Json;
using ChaseNet2.Transport;
using Serilog;

public class FileHost : ConnectionHandler, IMessageHandler
{
    FileSpec Spec;
    FileStream Stream;
    public FileHost(string specPath,string filePath)
    {
        var specText = File.ReadAllText(specPath);
        Spec = JsonSerializer.Deserialize<FileSpec>(specText);
            
        Stream = new FileStream(filePath, FileMode.Open);
    }

    public override Task OnAttached(ConnectionManager manager)
    {
        manager.Serializer.RegisterType(typeof(FilePartRequest));
        manager.Serializer.RegisterType(typeof(FilePartResponse));
        
        return Task.CompletedTask;
    }

    public override Task OnManagerConnect(Connection connection)
    {
        connection.RegisterMessageHandler(997,this);
        return Task.CompletedTask;
    }

    public override void ConnectionUpdate(Connection connection)
    {
    }

    public override void Update()
    {
    }

    public void HandleMessage(Connection connection, NetworkMessage message)
    {
        switch (message.Content)
        {
            case FilePartRequest request:
                if (request.FileName!=Spec.FileName)
                {
                    Log.Warning("Client requested file {0} but we are hosting {1}",request.FileName,Spec.FileName);
                    return;
                }
                var part = new FilePartResponse();
                part.FileName = Spec.FileName;
                part.Offset = request.Offset;
                    
                var buffer = new byte[request.Length];
                    
                Stream.Seek(request.Offset, SeekOrigin.Begin);
                Stream.Read(buffer, 0, request.Length);
                    
                part.Data = buffer;
                    
                connection.EnqueueMessage(MessageType.Reliable,998,part);
                Log.Information("Sent {0} bytes at offset {1} of file {2} to client",request.Length,request.Offset,Spec.FileName);
                break;
        }
    }
}