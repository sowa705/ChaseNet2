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
    // FileHost keeps its file open for the life of the process, so every test needs its own files.
    private readonly string _testFile = $"test-{Guid.NewGuid():N}.bin";
    private readonly string _downloadedFile = $"tmp-{Guid.NewGuid():N}.bin";

    public FileTransfer(ITestOutputHelper output)
    {
        Logger logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.TestOutput(output, LogEventLevel.Debug)
            .CreateLogger();

        Log.Logger = logger;
        
        //Create a dummy file to transfer
        var bytes = new byte[20670537]; //20MB but a weird number
        new Random().NextBytes(bytes);
        File.WriteAllBytes(_testFile, bytes);
    }

    private static bool IsFullyWritten(string path, long expectedLength)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return stream.Length == expectedLength;
        }
        catch (IOException)
        {
            return false; // missing yet, or still open for writing
        }
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
        var host = new FileHost(_testFile);
        hostCM.AttachHandler(host);
        
        // create file client
        var clientCM = new ConnectionManager();
        RegisterTypes(clientCM);
        var clientTrackerConnection = clientCM.CreateConnection(IPEndPoint.Parse("127.0.0.1:" + trackerPort));
        var clientSession = new SessionClient("TrackerSession", clientCM, clientTrackerConnection);
        clientSession.Connect();
        var client = new FileClient();
        clientCM.AttachHandler(client);

        // update all connections until the host and client are both registered with the tracker and see each other
        await Helpers.UpdateUntil(() =>
                trackerCM.Connections.Count == 2 && hostCM.Connections.Count == 2 && clientCM.Connections.Count == 2 &&
                hostCM.Connections.TrueForAll(x => x.State == ConnectionState.Connected) &&
                clientCM.Connections.TrueForAll(x => x.State == ConnectionState.Connected),
            trackerCM, hostCM, clientCM);

        return (tracker, host, client, trackerCM, hostCM, clientCM);
    }
    
    [Fact]
    public async Task ShouldConnect()
    {
        // arrange
        var (tracker, host, client, trackerCM, hostCM, clientCM) = await PrepareTrackerHostAndClient();
        
        // act
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
        await Helpers.UpdateUntil(() => client.DiscoveredFiles.Count == 1, trackerCM, hostCM, clientCM);

        // assert
        Assert.True(client.DiscoveredFiles.Count == 1);
    }
    
    [Fact]
    public async Task ShouldTransferFile()
    {
        // arrange
        var (tracker, host, client, trackerCM, hostCM, clientCM) = await PrepareTrackerHostAndClient();
        
        // act
        await Helpers.UpdateUntil(() => client.DiscoveredFiles.Count == 1, trackerCM, hostCM, clientCM);
        var file = client.DiscoveredFiles.First();
        client.StartTransfer(file.Item2, _downloadedFile);
        // The client closes the destination stream once every part has arrived, which is when it can be opened exclusively.
        await Helpers.UpdateUntil(() => IsFullyWritten(_downloadedFile, new FileInfo(_testFile).Length),
            TimeSpan.FromSeconds(30), trackerCM, hostCM, clientCM);
        
        // assert
        Assert.True(File.Exists(_downloadedFile));
        var original = File.ReadAllBytes(_testFile);
        var downloaded = File.ReadAllBytes(_downloadedFile);
        Assert.True(downloaded.SequenceEqual(original));
    }
}