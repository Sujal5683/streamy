using System;
using System.Security.Cryptography;
using System.Text;

namespace EnterpriseRemote.Common.Cryptography;

public static class CryptoUtils
{
    /// <summary>
    /// Generates a cryptographically secure 256-bit high-entropy invitation token formatted as INV-XXXX-XXXX-XXXX.
    /// </summary>
    public static string GenerateInvitationCode()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(6); // 12 hex characters
        string hex = Convert.ToHexString(bytes);
        return $"INV-{hex[..4]}-{hex.Substring(4, 4)}-{hex.Substring(8, 4)}";
    }

    /// <summary>
    /// Generates a cryptographically secure 4-digit numeric short PIN (0000–9999),
    /// suitable for quick attended enrollment without needing to type the full invitation code.
    /// </summary>
    public static string GenerateShortPin()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(2);
        int num = BitConverter.ToUInt16(bytes, 0) % 10000;
        return num.ToString("D4");
    }

    /// <summary>
    /// Computes the SHA-256 hash of a string, returning a lower-case hexadecimal digest.
    /// </summary>
    public static string ComputeSha256(string input)
    {
        byte[] inputBytes = Encoding.UTF8.GetBytes(input);
        byte[] hash = SHA256.HashData(inputBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Generates an ECDSA P-256 keypair, returning the private key in PKCS#8 PEM format and the public key in SPKI PEM format.
    /// </summary>
    public static (string PrivateKeyPem, string PublicKeyPem) GenerateEcdsaKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string privateKey = ecdsa.ExportPkcs8PrivateKeyPem();
        string publicKey = ecdsa.ExportSubjectPublicKeyInfoPem();
        return (privateKey, publicKey);
    }

    /// <summary>
    /// Signs data using an ECDSA P-256 private key with SHA-256.
    /// </summary>
    public static string SignData(string data, string privateKeyPem)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKeyPem);
        byte[] dataBytes = Encoding.UTF8.GetBytes(data);
        byte[] signature = ecdsa.SignData(dataBytes, HashAlgorithmName.SHA256);
        return Convert.ToBase64String(signature);
    }

    /// <summary>
    /// Verifies an ECDSA P-256 signature against data using the public key.
    /// </summary>
    public static bool VerifySignature(string data, string base64Signature, string publicKeyPem)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(publicKeyPem);
            byte[] dataBytes = Encoding.UTF8.GetBytes(data);
            byte[] signature = Convert.FromBase64String(base64Signature);
            return ecdsa.VerifyData(dataBytes, signature, HashAlgorithmName.SHA256);
        }
        catch
        {
            return false;
        }
    }
}
