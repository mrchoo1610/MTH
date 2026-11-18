using System.Security.Cryptography;
using System.Text;

namespace MiniTools.Contracts;

/// <summary>Signs/verifies manifests with ECDSA P-256 + SHA-256 (IEEE P1363 signature, base64).</summary>
public static class ManifestSigner
{
    /// <summary>Creates a new key pair: (privatePem PKCS#8, publicPem SPKI).</summary>
    public static (string PrivatePem, string PublicPem) GenerateKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportPkcs8PrivateKeyPem(), ecdsa.ExportSubjectPublicKeyInfoPem());
    }

    /// <summary>Returns the base64 signature. Does not modify the manifest.</summary>
    public static string Sign(ReleaseManifest m, string privateKeyPem)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(NormalizePem(privateKeyPem));
        var data = Encoding.UTF8.GetBytes(m.CanonicalPayload());
        var sig = ecdsa.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return Convert.ToBase64String(sig);
    }

    /// <summary>false when the signature is missing/invalid or the key is bad; never throws.</summary>
    public static bool Verify(ReleaseManifest m, string publicKeyPem)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(m.Signature) || string.IsNullOrWhiteSpace(publicKeyPem)) return false;
            var sig = Convert.FromBase64String(m.Signature);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(NormalizePem(publicKeyPem));
            var data = Encoding.UTF8.GetBytes(m.CanonicalPayload());
            return ecdsa.VerifyData(data, sig, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Accepts single-line PEMs that use a literal "\n" (common in .env files).</summary>
    public static string NormalizePem(string pem) =>
        pem.Trim().Trim('"').Replace("\\r", "").Replace("\\n", "\n");
}
