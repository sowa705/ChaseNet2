# ChaseNet2
Peer to peer networking library written in pure C#

## Advantages

* Real P2P system
* Powerful serialization system
* Custom protocol
  * Low latency - UDP based
  * Message acknowledgement and retransmission
  * Message compression
  * E2E encryption

## Basic Usage

Most important classes are `ConnectionManager` and `Connection`. ConnectionManager is used to create connections and listen for incoming connections. Connection is a point to point connection between two ConnectionManagers.

```csharp
// Create a connection manager
var connectionManager = new ConnectionManager(); // optionally pass a port number to listen on
connectionManager.StartBackgroundThread(); // start a background thread to handle network events

// Create a connection to another connection manager
var connection = await connectionManager.CreateConnection(IPEndPoint.Parse("127.0.0.1:1234"));

// Register the message class with the serialization system (Any class that can be serialized with Protobuf-net)
connectionManager.Serializer.RegisterType<string>();

// Send a message
// (message type, channel ID, message content)
var sentMessage = connection.EnqueueMessage(MessageType.Reliable | MessageType.Priority, 1234, "Hello !");

// Receive a message
connection.IncomingMessages.TryDequeue(out var message)
switch (message.Content)
{
    case string str:
        Console.WriteLine($"Received string: {str}");
        break;
}
```

## Projects included

* ChaseNet2 - Main library
* ChaseNet2.SimpleTracker - Simple tracker implementation
* ChaseNet2.FileTransfer - File transfer demo
* ChaseNet2.Tests - Unit tests

## TODO
* More tracker features
* External key exchange integration
* Documentation
