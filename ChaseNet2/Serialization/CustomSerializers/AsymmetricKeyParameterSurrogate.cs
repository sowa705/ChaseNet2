using Org.BouncyCastle.Crypto;
using ProtoBuf;

namespace ChaseNet2.Transport.Messages
{
    [ProtoContract]
    public class AsymmetricKeyParameterSurrogate
    {
        [ProtoMember(1)]
        public byte[] serializedKey { get; set; }

        public static implicit operator AsymmetricKeyParameterSurrogate(AsymmetricKeyParameter key)
        {
            return key != null ?
                new AsymmetricKeyParameterSurrogate
                {
                    serializedKey = CryptoHelper.SerializePublicKey(key)
                }
                : null;
        }

        public static implicit operator AsymmetricKeyParameter(AsymmetricKeyParameterSurrogate surrogate)
        {
            return CryptoHelper.DeserializePublicKey(surrogate.serializedKey);
        }
    }
}