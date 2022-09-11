using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using ChaseNet2.Extensions;
using ChaseNet2.Serialization;
using ChaseNet2.Transport;

namespace ChaseNet2.Session.Messages
{
    public class SessionUpdate : IStreamSerializable
    {
        public List<ConnectionTarget> Peers { get; set; }
        
        public SessionUpdate()
        {
            Peers = new List<ConnectionTarget>();
        }

        public int Serialize(BinaryWriter writer)
        {
            return Peers.Serialize(writer);
        }

        public void Deserialize(BinaryReader reader)
        {
            Peers.Deserialize(reader);
        }
    }
}