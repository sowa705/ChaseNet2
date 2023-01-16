using System.Diagnostics;
using System.Net;
using ChaseNet2.FileTransfer;
using ChaseNet2.Session;
using ChaseNet2.Transport;
using Mono.Nat;
using Serilog;
using Serilog.Core;

public class Program
{
    static bool Ready = false;
    static ConnectionManager Manager;
    public static async Task Main(string[] args)
    {
        Logger logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .CreateLogger();

        Log.Logger = logger;
        
        Manager = new ConnectionManager();
        await Manager.Update();

        // hehe
        var trackerConnection = Manager.CreateConnection(IPEndPoint.Parse("13.80.242.30:2137"));

        SessionClient client = new SessionClient("TrackerSession",Manager,trackerConnection);
        
        client.Connect();
        
        int counter=0;
        while (true)
        {
            if (counter%30==0)
            {
                Console.WriteLine($"Host statistics: {Manager.Statistics}");
            }
            
            await Task.Delay(100);
            await Manager.Update();
            counter++;
        }
    }
}