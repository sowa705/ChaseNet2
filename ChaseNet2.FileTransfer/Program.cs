using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ChaseNet2.FileTransfer;
using ChaseNet2.Session;
using ChaseNet2.Transport;
using Mono.Nat;
using Serilog;
using Serilog.Core;

public class Program
{
    static ConnectionManager Manager;
    static SessionClient Client;
    public static async Task Main(string[] args)
    {
        InitLogger();
        await InitNetwork();
        
        Console.WriteLine("Starting with args:" + string.Join(" ", args));

        if (args[0] == "Host")
        {
            FileHost host = new FileHost(args[1]);
            
            Manager.AttachHandler(host);

            while (true)
            {
                await Task.Delay(200);
            }
        }
        
        if (args[0] == "Client")
        {
            FileClient client = new FileClient();
            Manager.AttachHandler(client);

            while (true)
            {
                Console.Write("Command>");
                var cmd = Console.ReadLine();

                if (cmd.StartsWith("list"))
                {
                    foreach (var file in client.DiscoveredFiles)
                    {
                        Console.WriteLine("File: {0} - {1}", file.Item2.FileName, file.Item1.ConnectionId);
                    }
                }

                if (cmd.StartsWith("download"))
                {
                    var filename = cmd.Split(' ')[1];
                    var dest = cmd.Split(' ')[2];
                    
                    var file = client.DiscoveredFiles.FirstOrDefault(x => x.Item2.FileName == filename);
                    
                    client.StartTransfer(file.Item2, dest);
                }
            }
        }
    }

    private static async Task InitNetwork()
    {
        Manager = new ConnectionManager();
        await Manager.Update();

        // hehe
        var trackerConnection = Manager.CreateConnection(IPEndPoint.Parse("127.0.0.1:2137"));

        Client = new SessionClient("TrackerSession", Manager, trackerConnection);
        
        // Register types
        Manager.Serializer.RegisterType<FileSpec>();
        Manager.Serializer.RegisterType<FilePartSpec>();
        Manager.Serializer.RegisterType<FilePartRequest>();
        Manager.Serializer.RegisterType<FilePartResponse>();

        NetworkThread();

        Client.Connect();

        if (Client.State != SessionClientState.Connected)
        {
            Log.Fatal("Failed to connect to tracker");
        }
    }

    private static void InitLogger()
    {
        Logger logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .CreateLogger();

        Log.Logger = logger;
    }

    private static async Task CreateSpec(string[] args)
    {
        var spec = await FileSpec.Create(args[1]);
        var serialized = JsonSerializer.Serialize(spec, new JsonSerializerOptions { WriteIndented = true });
        var filename = spec.FileName + ".spec";
        await File.WriteAllTextAsync(filename, serialized);
    }

    public static async Task NetworkThread()
    {
        int counter=0;
        while (true)
        {
            await Task.Delay(50);
            await Manager.Update();
            counter++;
        }
    }
}