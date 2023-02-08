using System.Text.Json;
using ChaseNet2.Relay;
using ChaseNet2.Session;
using ChaseNet2.SimpleTracker;
using ChaseNet2.Transport;
using Serilog;
using Serilog.Core;

var settings = new TrackerSettings();
if (File.Exists("tracker.json"))
{
    settings = JsonSerializer.Deserialize<TrackerSettings>(File.ReadAllText("tracker.json")) ?? new TrackerSettings();
}

if (settings.DebugLogging)
{
    var logger = new LoggerConfiguration()
        .MinimumLevel.Debug()
        .WriteTo.Console()
        .CreateLogger();
    Log.Logger = logger;
}
else
{
    var logger = new LoggerConfiguration()
        .MinimumLevel.Warning()
        .WriteTo.Console()
        .CreateLogger();
    Log.Logger = logger;
}

var cm = new ConnectionManager(settings.Port);
cm.Settings.TargetUpdateRate = settings.UpdateRate;
cm.StartBackgroundThread();

var st = new SessionTracker();
st.SessionName = "TrackerSession";
cm.AttachHandler(st);

if (settings.EnableRelay)
{
    var relayHost = new RelayHost();
    cm.AttachHandler(relayHost);
}

int counter = 0;
while (true)
{
    if (counter % 10 == 0)
    {
        Console.WriteLine($"Host statistics: {cm.Statistics}");
    }

    await Task.Delay(1000);
    counter++;
}