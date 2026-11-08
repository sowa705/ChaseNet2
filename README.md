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
using var connectionManager = new ConnectionManager(); // optionally pass a port number to listen on
connectionManager.Start(); // run the network on its own thread

// Create a connection to another connection manager
var connection = connectionManager.CreateConnection(IPEndPoint.Parse("127.0.0.1:1234"));

// Register the message class with the serialization system (any [ProtoContract] class)
connectionManager.Serializer.RegisterType<ChatMessage>();

// Send a message from any thread. The content is serialized right away.
// (message type, channel ID, message content)
var sentMessage = connection.EnqueueMessage(MessageType.Reliable | MessageType.Priority, 1234, new ChatMessage("Hello !"));
bool delivered = await connection.WaitForDeliveryAsync(sentMessage);

// Handle messages on a channel; Queued handlers run inside Dispatch()
connection.RegisterMessageHandler(1234, myHandler);

// In your game/app loop: run queued handlers and ConnectionHandler callbacks on this thread.
// Messages on channels without a handler land in connection.IncomingMessages.
connectionManager.Dispatch();
```

### Threading

* **Threaded mode** (`Start()`): a dedicated network thread owns the socket and all transport state (receive, acks,
  resends, pings, split messages, handshakes). Connections stay alive and reliable messages keep being acknowledged even
  when the application thread stalls for longer than `TransportSettings.PongTimeout`. The application calls `Dispatch()`
  (or `Update()`, which only dispatches while the thread runs) to receive messages and callbacks on its own thread.
  `Stop()` returns to manual mode; `Dispose()` stops the thread, closes the socket and fails undelivered reliable messages.
* **Manual mode** (no `Start()`): call `Update()` regularly; it pumps the network and dispatches on the calling thread.
* `RegisterMessageHandler(channel, handler, MessageDispatchMode.Queued)` (default) runs the handler on the dispatching
  thread. `MessageDispatchMode.Inline` runs it on the network thread: it must be thread-safe and must not block.
* While queued, sequenced messages are coalesced (only the newest per connection and channel is dispatched), and
  unreliable messages are dropped once `TransportSettings.MaxPendingDispatchItems` is reached.
* `Connections` is an immutable snapshot; `CreateConnection`, `AttachConnectionAsync`, `RemoveConnection`,
  `EnqueueMessage`, handler registration and type registration may be called from any thread.

## Projects included

* ChaseNet2 - Main library
* ChaseNet2.SimpleTracker - Simple tracker implementation
* ChaseNet2.FileTransfer - File transfer demo
* ChaseNet2.Tests - Unit tests

## TODO
* More tracker features
* External key exchange integration
* Documentation
