using System.Diagnostics;
using System.Net;
using ChaseNet2.Session;
using ChaseNet2.Transport;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Exceptions;

Logger logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .CreateLogger();

Log.Logger = logger;

ConnectionManager host = new ConnectionManager(6000);
SessionTracker tracker = new SessionTracker(host);

tracker.SessionName="MySession";

host.StartBackgroundThread();

List<(ConnectionManager,SessionClient)> clients = new List<(ConnectionManager,SessionClient)>();

ConnectionManager client = new ConnectionManager();
var connection = client.CreateConnection(IPEndPoint.Parse("127.0.0.1:6000"), host.PublicKey);
//var connection2 = client.CreateConnection(IPEndPoint.Parse("127.0.0.1:6000"), host.PublicKey);

    
SessionClient sessionClient = new SessionClient("MySession", client, connection);
//SessionClient sessionClient2 = new SessionClient("MySession", client, connection2);

client.StartBackgroundThread();

await sessionClient.Connect();
//await sessionClient2.Connect();


int counter=0;

while (true)
{
    counter++;

    if (counter%50==0)
    {
        Console.WriteLine($"Host statistics: {host.Statistics}");
        
        foreach (var c in clients)
        {
            Console.WriteLine($"Client statistics: {c.Item1.Statistics}");
        }
    }
    await Task.Delay(16);
}