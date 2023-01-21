# FileTransfer Demo

Send files directly from one computer to another.

## Usage

* Set up ChaseNet2.SimpleTracker on a public IP address. `dotnet run --project ChaseNet2.SimpleTracker`
* Generate file spec on the sender side. `dotnet run --project ChaseNet2.FileTransfer -- spec <file path>`
* Send the file spec to the receiver (preferably over a secure channel).
* Start a file host on the sender side. `dotnet run --project ChaseNet2.FileTransfer -- host <tracker endpoint> <spec path> <file path>`
* Start a file receiver on the receiver side. `dotnet run --project ChaseNet2.FileTransfer -- receive <tracker endpoint> <spec path>`

## How it works

* Tracker sets up point to point connections between peers (This is its only job, actual transfer is not handled by the tracker)
* The receiver will send requests to other peers searching for the file.
* The host will return file chunks to the receiver.
* The receiver will write the file chunks to the file and check the hash to make sure the file is correct.

## Security

Peer public key authenticity is not checked in this demo so MitM listening attacks are possible.

File spec contains hashes for every chunk so if chunks are modified the hash will not match and the file will be rejected.