using System.Net;
using ChaseNet2;

ConnectionManager host = new ConnectionManager(5000);

host.AcceptNewConnections = true;

List<ConnectionManager> clients = new List<ConnectionManager>();

for (int i = 0; i < 1; i++)
{
    ConnectionManager client = new ConnectionManager();
    var connection = client.CreateConnection(IPEndPoint.Parse("127.0.0.1:5000"), host.PublicKey);
    
    clients.Add(client);
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
    await Task.Delay(50);
    Task.WaitAll(clients.Select(x=>Task.Run(() => { x.Update(); })).ToArray());
}