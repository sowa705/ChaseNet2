using System;

namespace ChaseNet2.Transport;

public class InvalidNetworkMessageTypeException : Exception
{
    public InvalidNetworkMessageTypeException(string s)
    {
    }
}