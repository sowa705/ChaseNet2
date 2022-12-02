using System;
using System.Security.Cryptography;
using Org.BouncyCastle.Asn1.Nist;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using ECPoint = Org.BouncyCastle.Math.EC.ECPoint;

namespace ChaseNet2
{
    public class CryptoHelper
    {
        public static AsymmetricCipherKeyPair GenerateKeyPair()
        {
            X9ECParameters x9EC = NistNamedCurves.GetByName("P-521");
            ECDomainParameters ecDomain = new ECDomainParameters(x9EC.Curve, x9EC.G, x9EC.N, x9EC.H, x9EC.GetSeed());

            ECKeyPairGenerator g = (ECKeyPairGenerator)GeneratorUtilities.GetKeyPairGenerator("ECDH");
            g.Init(new ECKeyGenerationParameters(ecDomain, new SecureRandom()));

            AsymmetricCipherKeyPair keyPair = g.GenerateKeyPair();
            return keyPair;
        }
        
        public static byte[] SerializePublicKey(AsymmetricKeyParameter publicKey)
        {
            if (publicKey.IsPrivate)
            {
                throw new ArgumentException("Public key expected");
            }
            ECPublicKeyParameters ecPublicKey = (ECPublicKeyParameters)publicKey;
            return ecPublicKey.Q.GetEncoded();
        }
        
        public static AsymmetricKeyParameter DeserializePublicKey(byte[] publicKey)
        {
            var x9EC = NistNamedCurves.GetByName("P-521");
            var ecDomain = new ECDomainParameters(x9EC.Curve, x9EC.G, x9EC.N, x9EC.H, x9EC.GetSeed());
            var q = x9EC.Curve.DecodePoint(publicKey);
            return new ECPublicKeyParameters(q, ecDomain);
        }

        public static byte[] GenerateDHKey(AsymmetricKeyParameter ourPrivateKey, AsymmetricKeyParameter theirPublicKey)
        {
            ECDHBasicAgreement agreement = new ECDHBasicAgreement();
            agreement.Init(ourPrivateKey);
            BigInteger key = agreement.CalculateAgreement(theirPublicKey);
            // get a hash of the key
            byte[] keyBytes = key.ToByteArrayUnsigned();
            SHA256Managed sha = new SHA256Managed();
            sha.ComputeHash(keyBytes, 0, keyBytes.Length);
            return sha.Hash;
        }
    }
}