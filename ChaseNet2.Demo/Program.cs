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


for (int i = 0; i < 1; i++)
{
    ConnectionManager client = new ConnectionManager();
    var connection = client.CreateConnection(IPEndPoint.Parse("127.0.0.1:6000"));
    SessionClient sessionClient = new SessionClient("MySession", client, connection);
    
    client.StartBackgroundThread();
    sessionClient.Connect();
}

int counter=0;

while (true)
{
    counter++;

    if (counter%50==0)
    {
        Console.WriteLine($"Host statistics: {host.Statistics}");
    }
    await Task.Delay(16);
}