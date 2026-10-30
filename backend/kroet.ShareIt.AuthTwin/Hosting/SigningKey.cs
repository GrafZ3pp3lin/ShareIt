using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace kroet.ShareIt.AuthTwin.Hosting;

/// <summary>The realm's RS256 key, generated per run. Tokens from an earlier run don't verify.</summary>
internal sealed class SigningKey : IDisposable
{
    private readonly RSAParameters _publicKey;

    public SigningKey()
    {
        // Generates the key now: RSA creates it lazily, and a first signing racing a first export
        // could otherwise end up with two different keys.
        _publicKey = Rsa.ExportParameters(includePrivateParameters: false);
    }

    public RSA Rsa { get; } = RSA.Create(2048);

    public string KeyId { get; } = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16));

    public object ToJwk() => new
    {
        kid = KeyId,
        kty = "RSA",
        alg = "RS256",
        use = "sig",
        n = WebEncoders.Base64UrlEncode(_publicKey.Modulus!),
        e = WebEncoders.Base64UrlEncode(_publicKey.Exponent!),
    };

    public void Dispose() => Rsa.Dispose();
}
