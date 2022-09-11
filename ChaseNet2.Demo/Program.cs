using System.Net;
using ChaseNet2.Session;
using ChaseNet2.Transport;

ConnectionManager host = new ConnectionManager(5000);
SessionTracker tracker = new SessionTracker(host);
tracker.SessionName="MySession";

List<(ConnectionManager,SessionClient)> clients = new List<(ConnectionManager,SessionClient)>();

for (int i = 0; i < 1; i++)
{
    ConnectionManager client = new ConnectionManager();
    var connection = client.CreateConnection(new ConnectionTarget() { EndPoint = IPEndPoint.Parse("127.0.0.1:5000"), PublicKey = host.PublicKey});
    
    SessionClient sessionClient = new SessionClient("MySession",connection);
    clients.Add((client,sessionClient));
}



int counter=0;

while (true)
{
    counter++;

    if (counter%5==0)
    {
        Console.WriteLine($"Host statistics: {host.Statistics}");
    }
    await Task.Delay(50);
    host.Update();
    tracker.Update();
    await Task.Delay(50);
    //Task.WaitAll(clients.Select(x=>Task.Run(() => { x.Item1.Update(); x.Item2.Update(); })).ToArray());
    foreach (var client in clients)
    {
        client.Item1.Update();
        client.Item2.Update();
    }
}