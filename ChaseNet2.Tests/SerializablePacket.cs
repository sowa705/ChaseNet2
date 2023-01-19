using ChaseNet2.Serialization;

namespace ChaseNet2.Tests;

public class DummyMessage : IStreamSerializable
{
    public DummyMessage()
    {
        
    }
    protected bool Equals(DummyMessage other)
    {
        return Data.Equals(other.Data);
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((DummyMessage)obj);
    }

    public override int GetHashCode()
    {
        return Data.GetHashCode();
    }

    public byte[] Data { get; set; }
    
    public DummyMessage(int dataLength)
    {
        Data = new byte[dataLength];
        // Fill the data with random bytes
        new Random().NextBytes(Data);
    }
    public int Serialize(BinaryWriter writer)
    {
        writer.Write(Data.Length);
        writer.Write(Data);
        return Data.Length + sizeof(int);
    }

    public void Deserialize(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        Data = reader.ReadBytes(length);
    }

    public static bool operator == (DummyMessage a, DummyMessage b)
    {
        if (a.Data.Length != b.Data.Length)
            return false;
        for (int i = 0; i < a.Data.Length; i++)
        {
            if (a.Data[i]!=b.Data[i])
                return false;
        }
        return true;
    }

    public static bool operator!=(DummyMessage a, DummyMessage b)
    {
        return !(a == b);
    }
}