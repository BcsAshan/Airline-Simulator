using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Pss.Identity.Api.Infrastructure;

/// <summary>
/// RS256 signing key. Loaded from (or generated into) Jwt:KeyPath so tokens survive restarts.
/// TODO(prod): load from Key Vault / K8s secret and support key rotation (multiple JWKS entries).
/// </summary>
public sealed class SigningKeyStore
{
    public SigningKeyStore(string keyPath)
    {
        var rsa = RSA.Create(2048);
        if (File.Exists(keyPath))
        {
            rsa.ImportFromPem(File.ReadAllText(keyPath));
        }
        else
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(keyPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(keyPath, rsa.ExportRSAPrivateKeyPem());
        }

        var parameters = rsa.ExportParameters(false);
        KeyId = Convert.ToHexString(SHA256.HashData(parameters.Modulus!))[..16].ToLowerInvariant();
        Key = new RsaSecurityKey(rsa) { KeyId = KeyId };
        Modulus = Base64UrlEncoder.Encode(parameters.Modulus);
        Exponent = Base64UrlEncoder.Encode(parameters.Exponent);
        Credentials = new SigningCredentials(Key, SecurityAlgorithms.RsaSha256);
    }

    public string KeyId { get; }
    public RsaSecurityKey Key { get; }
    public SigningCredentials Credentials { get; }
    public string Modulus { get; }
    public string Exponent { get; }
}
