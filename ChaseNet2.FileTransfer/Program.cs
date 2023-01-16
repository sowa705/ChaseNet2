using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
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
        if (args[0]=="spec")
        {
            await CreateSpec(args);
            return;
        }
        
        InitLogger();
        await InitNetwork();

        if (args[0] == "Host")
        {
            FileHost host = new FileHost(args[1],args[2]);
            
            Manager.AttachHandler(host);
        }
        
        await Task.Delay(-1);
    }

    private static async Task InitNetwork()
    {
        Manager = new ConnectionManager();
        await Manager.Update();

        // hehe
        var trackerConnection = Manager.CreateConnection(IPEndPoint.Parse("127.0.0.1:2137"));

        Client = new SessionClient("TrackerSession", Manager, trackerConnection);

        NetworkThread();

        await Client.Connect();

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
            if (counter%30==0)
            {
                Console.WriteLine($"Host statistics: {Manager.Statistics}");
            }
            
            await Task.Delay(50);
            await Manager.Update();
            counter++;
        }
    }
}