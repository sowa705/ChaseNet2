using System.Net;
using ChaseNet2.FileTransfer;
using ChaseNet2.Session;
using ChaseNet2.Transport;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;
using Xunit.Abstractions;

namespace ChaseNet2.Tests;

public class FileTransfer
{
    public FileTransfer(ITestOutputHelper output)
    {
        Logger logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.TestOutput(output, LogEventLevel.Debug)
            .CreateLogger();

        Log.Logger = logger;
    }

    void RegisterTypes(ConnectionManager cm)
    {
        // Register types
        cm.Serializer.RegisterType<FileSpec>();
        cm.Serializer.RegisterType<FilePartSpec>();
        cm.Serializer.RegisterType<FilePartRequest>();
        cm.Serializer.RegisterType<FilePartResponse>();
    }

    public async Task<(SessionTracker tracker,FileHost host, FileClient client, ConnectionManager trackerCM, 
    ConnectionManager hostCM, ConnectionManager clientCM)> 
    PrepareTrackerHostAndClient()
    {
        // create tracker
        var trackerPort = Random.Shared.Next(10000, 20000);
        
        var trackerCM = new ConnectionManager(trackerPort);
        var tracker = new SessionTracker();
        tracker.SessionName = "TrackerSession";
        trackerCM.AttachHandler(tracker);
        
        // create file host
        var hostCM = new ConnectionManager();
        RegisterTypes(hostCM);
        var hostTrackerConnection = hostCM.CreateConnection(IPEndPoint.Parse("127.0.0.1:" + trackerPort));
        var hostSession = new SessionClient("TrackerSession", hostCM, hostTrackerConnection);
        hostSession.Connect();
        var host = new FileHost("dummyFile.bin");
        hostCM.AttachHandler(host);
        
        // create file client
        var clientCM = new ConnectionManager();
        RegisterTypes(clientCM);
        var clientTrackerConnection = clientCM.CreateConnection(IPEndPoint.Parse("127.0.0.1:" + trackerPort));
        var clientSession = new SessionClient("TrackerSession", clientCM, clientTrackerConnection);
        clientSession.Connect();
        var client = new FileClient();
        clientCM.AttachHandler(client);

        // update all connections
        await Helpers.UpdateManagers(trackerCM, hostCM, clientCM);
        
        return (tracker, host, client, trackerCM, hostCM, clientCM);
    }
    
    [Fact]
    public async Task ShouldConnect()
    {
        // arrange
        var (tracker, host, client, trackerCM, hostCM, clientCM) = await PrepareTrackerHostAndClient();
        
        // act
        await Helpers.UpdateManagers(trackerCM, hostCM, clientCM);
        await Helpers.UpdateManagers(trackerCM, hostCM, clientCM);

        // assert
        Assert.True(trackerCM.Connections.Count == 2);
        Assert.True(hostCM.Connections.Count == 2);
        Assert.True(clientCM.Connections.Count == 2);
        
        Assert.True(hostCM.Connections.TrueForAll(x=>x.State==ConnectionState.Connected));
        Assert.True(clientCM.Connections.TrueForAll(x=>x.State==ConnectionState.Connected));
    }
    
    [Fact]
    public async Task ShouldGetFileSpec()
    {
        // arrange
        var (tracker, host, client, trackerCM, hostCM, clientCM) = await PrepareTrackerHostAndClient();
        
        // act
        await Helpers.UpdateManagers(trackerCM, hostCM, clientCM);
        await Helpers.UpdateManagers(trackerCM, hostCM, clientCM);

        // assert
        Assert.True(client.DiscoveredFiles.Count == 1);
    }
    
    [Fact]
    public async Task ShouldTransferFile()
    {
        // arrange
        File.Delete("tmp.bin");
        var (tracker, host, client, trackerCM, hostCM, clientCM) = await PrepareTrackerHostAndClient();
        
        // act
        await Helpers.UpdateManagers(trackerCM, hostCM, clientCM);
        await Helpers.UpdateManagers(trackerCM, hostCM, clientCM);
        var file = client.DiscoveredFiles.First();
        client.StartTransfer(file.Item2,"tmp.bin");
        for (int i = 0; i < 5; i++)
        {
            await Helpers.UpdateManagers(trackerCM, hostCM, clientCM);
        }
        
        // assert
        Assert.True(File.Exists("tmp.bin"));
    }
}