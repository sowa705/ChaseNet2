using ChaseNet2.Serialization;
using ProtoBuf;

namespace ChaseNet2.Tests;
[ProtoContract]
public class DummyMessage
{
    [ProtoMember(1)]
    public byte[] Data { get; set; }

    public DummyMessage()
    {
    }
    public DummyMessage(int dataLength)
    {
        Data = new byte[dataLength];
        // Fill the data with random bytes
        new Random().NextBytes(Data);
    }

    public static bool operator ==(DummyMessage a, DummyMessage b)
    {
        if (a.Data.Length != b.Data.Length)
            return false;
        for (int i = 0; i < a.Data.Length; i++)
        {
            if (a.Data[i] != b.Data[i])
                return false;
        }
        return true;
    }

    public static bool operator != (DummyMessage a, DummyMessage b)
    {
        return !(a == b);
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (ReferenceEquals(obj, null))
        {
            return false;
        }

        return this == obj;
    }
}