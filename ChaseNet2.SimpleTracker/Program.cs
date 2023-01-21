using ChaseNet2.Session;
using ChaseNet2.Transport;
using Serilog;
using Serilog.Core;

Logger logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .CreateLogger();

Log.Logger = logger;

ConnectionManager cm = new ConnectionManager(2137);
SessionTracker st = new SessionTracker();
cm.AttachHandler(st);

st.SessionName = "TrackerSession";

int counter = 0;
while (true)
{
    if (counter % 30 == 0)
    {
        Console.WriteLine($"Host statistics: {cm.Statistics}");
    }

    await Task.Delay(100);
    await cm.Update();
    counter++;
}