using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using kroet.ShareIt.AuthTwin.Hosting;
using Microsoft.AspNetCore.WebUtilities;

namespace kroet.ShareIt.AuthTwin.Oidc;

/// <summary>RS256 JWTs signed with the realm key. Claims with a null value are left out.</summary>
internal static class Jwt
{
    public static string Sign(IReadOnlyDictionary<string, object?> claims, SigningKey key)
    {
        var header = Encode(new Dictionary<string, object?> { ["alg"] = "RS256", ["typ"] = "JWT", ["kid"] = key.KeyId });
        var payload = Encode(claims.Where(c => c.Value is not null).ToDictionary());
        var input = $"{header}.{payload}";
        var signature = key.Rsa.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{input}.{WebEncoders.Base64UrlEncode(signature)}";
    }

    /// <summary>The claims of a token this twin signed in this run; null for anything else.</summary>
    public static JsonElement? Read(string token, SigningKey key)
    {
        var parts = token.Split('.');
        if (parts.Length != 3) return null;
        try
        {
            var signed = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
            if (!key.Rsa.VerifyData(signed, WebEncoders.Base64UrlDecode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) return null;
            var payload = JsonSerializer.Deserialize<JsonElement>(WebEncoders.Base64UrlDecode(parts[1]));
            return payload.ValueKind == JsonValueKind.Object ? payload : null;
        }
        catch (Exception e) when (e is FormatException or JsonException or CryptographicException)
        {
            return null;
        }
    }

    public static string? String(this JsonElement claims, string name) =>
        claims.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>OIDC at_hash: left half of the SHA-256 of the access token.</summary>
    public static string AtHash(string accessToken)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(accessToken));
        return WebEncoders.Base64UrlEncode(hash[..(hash.Length / 2)]);
    }

    private static string Encode(Dictionary<string, object?> values) =>
        WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(values));
}
