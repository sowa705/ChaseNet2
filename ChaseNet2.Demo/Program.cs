using System.Diagnostics;
using System.Net;
using ChaseNet2.Session;
using ChaseNet2.Transport;

ConnectionManager host = new ConnectionManager(5000);
SessionTracker tracker = new SessionTracker(host);
SessionTracker tracker2 = new SessionTracker(host);

tracker.SessionName="MySession";
tracker2.SessionName="MySession2";

host.AttachHandler(tracker);
host.AttachHandler(tracker2);

host.StartBackgroundThread();

List<(ConnectionManager,SessionClient)> clients = new List<(ConnectionManager,SessionClient)>();

for (int i = 0; i < 1000; i++)
{
    ConnectionManager client = new ConnectionManager();
    var connection = client.CreateConnection(IPEndPoint.Parse("127.0.0.1:5000"), host.PublicKey);
    var connection2 = client.CreateConnection(IPEndPoint.Parse("127.0.0.1:5000"), host.PublicKey);

    
    SessionClient sessionClient = new SessionClient("MySession",connection);
    client.AttachHandler(sessionClient);
    clients.Add((client,sessionClient));
    
    SessionClient sessionClient2 = new SessionClient("MySession2",connection2);
    client.AttachHandler(sessionClient2);
    
    client.StartBackgroundThread();
}



int counter=0;

while (true)
{
    counter++;

    if (counter%50==0)
    {
        Console.WriteLine($"Host statistics: {host.Statistics}");
        
        foreach (var client in clients)
        {
            //Console.WriteLine($"Client statistics: {client.Item1.Statistics}");
        }
    }
    await Task.Delay(16);
}