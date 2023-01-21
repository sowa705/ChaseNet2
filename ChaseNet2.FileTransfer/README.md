# FileTransfer Demo

Send files directly from one computer to another.

## Usage

* Set up ChaseNet2.SimpleTracker on a public IP address. `dotnet run --project ChaseNet2.SimpleTracker`
* Start a file host on the sender side. `dotnet run --project ChaseNet2.FileTransfer -- <tracker endpoint> Host <file path>`
* Host will start broadcasting the file spec to other peers.
* Start a file receiver on the receiver side. `dotnet run --project ChaseNet2.FileTransfer -- <tracker endpoint> Client`
* Wait until client receives a file broadcast from the host.
* use the `list` command to see the list of files.
* use the `download <file name> <destination path>` command to download a file.

## How it works

* Tracker is used to discover peers.
* File spec is a JSON object that contains the file name and parts with their hashes.
* File spec is broadcasted to other peers.
* Peers can request parts of the file from other peers.

## Security

Peer public key authenticity is not checked in this demo so MitM listening attacks are possible.

File spec contains hashes for every chunk so if chunks are modified the hash will not match and the file will be rejected.