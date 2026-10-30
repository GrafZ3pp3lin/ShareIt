using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace kroet.ShareIt.AuthTwin.Test;

/// <summary>A browser-like ES256 DPoP key used by the token endpoint tests.</summary>
internal sealed class DpopKey : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _x;
    private readonly string _y;

    public DpopKey()
    {
        var parameters = _key.ExportParameters(includePrivateParameters: false);
        _x = Base64UrlEncoder.Encode(parameters.Q.X!);
        _y = Base64UrlEncoder.Encode(parameters.Q.Y!);
        Jkt = Thumbprint(_x, _y);
    }

    public string Jkt { get; }

    public string Proof(string htu, string method, string? accessToken = null)
    {
        var payload = new Dictionary<string, object>
        {
            ["jti"] = Guid.NewGuid().ToString(),
            ["htm"] = method,
            ["htu"] = htu,
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        if (accessToken is not null)
            payload["ath"] = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(accessToken)));

        var header = new Dictionary<string, object>
        {
            ["alg"] = "ES256",
            ["typ"] = "dpop+jwt",
            ["jwk"] = new Dictionary<string, string>
            {
                ["crv"] = "P-256",
                ["kty"] = "EC",
                ["x"] = _x,
                ["y"] = _y,
            },
        };

        var input = $"{Encode(header)}.{Encode(payload)}";
        var signature = _key.SignData(
            Encoding.ASCII.GetBytes(input),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{input}.{Base64UrlEncoder.Encode(signature)}";
    }

    public void Dispose() => _key.Dispose();

    public static string Thumbprint(string x, string y)
    {
        var canonical = $$"""{"crv":"P-256","kty":"EC","x":"{{x}}","y":"{{y}}"}""";
        return Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string Encode(object value) =>
        Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(value));
}
