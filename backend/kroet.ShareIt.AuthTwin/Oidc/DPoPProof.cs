using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using kroet.ShareIt.AuthTwin.Store;
using Microsoft.AspNetCore.WebUtilities;

namespace kroet.ShareIt.AuthTwin.Oidc;

internal sealed record DPoPResult(bool Present, string? Thumbprint, string? Error)
{
    public static readonly DPoPResult Absent = new(false, null, null);
}

/// <summary>
/// Checks a DPoP proof (RFC 9449) the way Keycloak 26.7 does, with its error texts. ES256 only: that's
/// what browsers sign with, and every other algorithm is refused rather than half-supported.
/// </summary>
internal static class DPoPProof
{
    // Measured on Keycloak: a proof up to 20 s old or 15 s ahead is accepted.
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan MaxAhead = TimeSpan.FromSeconds(15);

    private const string VerificationFailure = "DPoP header verification failure";

    public static DPoPResult Validate(HttpRequest request, string expectedHtu, TimeProvider time, AuthStore store)
    {
        var headers = request.Headers["DPoP"];
        if (headers.Count == 0) return DPoPResult.Absent;
        if (headers.Count > 1) return Fail(VerificationFailure);

        var parts = headers[0]!.Split('.');
        if (parts.Length != 3) return Fail(VerificationFailure);

        JsonElement header, payload;
        byte[] signature;
        try
        {
            header = JsonSerializer.Deserialize<JsonElement>(WebEncoders.Base64UrlDecode(parts[0]));
            payload = JsonSerializer.Deserialize<JsonElement>(WebEncoders.Base64UrlDecode(parts[1]));
            signature = WebEncoders.Base64UrlDecode(parts[2]);
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return Fail(VerificationFailure);
        }
        if (header.ValueKind != JsonValueKind.Object || payload.ValueKind != JsonValueKind.Object) return Fail(VerificationFailure);

        var typ = StringOf(header, "typ");
        if (typ != "dpop+jwt") return Fail($"Invalid or missing type in DPoP header: {typ ?? "null"}");

        if (!header.TryGetProperty("jwk", out var jwk) || jwk.ValueKind != JsonValueKind.Object) return Fail(VerificationFailure);
        if (jwk.TryGetProperty("d", out _)) return Fail("Private key material must not be present in DPoP JWK");

        var alg = StringOf(header, "alg");
        var kty = StringOf(jwk, "kty");
        var crv = StringOf(jwk, "crv");
        if (alg != "ES256" || kty != "EC" || crv != "P-256")
            return Fail($"Key with algorithm {alg ?? "null"} and type {kty ?? "null"} is incorrect for provider algorithm {alg ?? "null"}");

        var x = StringOf(jwk, "x");
        var y = StringOf(jwk, "y");
        if (x is null || y is null) return Fail(VerificationFailure);

        try
        {
            using var key = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = WebEncoders.Base64UrlDecode(x), Y = WebEncoders.Base64UrlDecode(y) },
            });
            var signed = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
            if (!key.VerifyData(signed, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                return Fail("DPoP verification failure: org.keycloak.exceptions.TokenSignatureInvalidException: Invalid token signature");
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            return Fail(VerificationFailure);
        }

        var jti = StringOf(payload, "jti");
        var htm = StringOf(payload, "htm");
        var htu = StringOf(payload, "htu");
        if (jti is null || htm is null || htu is null || !payload.TryGetProperty("iat", out var iatElement)
            || !iatElement.TryGetInt64(out var iat))
            return Fail("DPoP mandatory claims are missing");

        if (htm != request.Method) return Fail("DPoP HTTP method mismatch");
        if (!SameUrl(htu, expectedHtu)) return Fail("DPoP HTTP URL mismatch");

        // Whole seconds, like the iat claim itself.
        var now = time.GetUtcNow().ToUnixTimeSeconds();
        if (iat < now - (long)MaxAge.TotalSeconds || iat > now + (long)MaxAhead.TotalSeconds) return Fail("DPoP proof is not active");

        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(iat);
        if (!store.TryUseProof(jti, issuedAt + MaxAge + MaxAhead)) return Fail("DPoP proof has already been used");

        return new DPoPResult(true, Thumbprint(crv, kty, x, y), null);
    }

    /// <summary>RFC 7638: SHA-256 over the required members in lexicographic order.</summary>
    public static string Thumbprint(string crv, string kty, string x, string y)
    {
        var canonical = $$"""{"crv":"{{crv}}","kty":"{{kty}}","x":"{{x}}","y":"{{y}}"}""";
        return WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>Query and fragment don't count, the host is case-insensitive; scheme, port and path must match.</summary>
    private static bool SameUrl(string htu, string expected)
    {
        if (!Uri.TryCreate(htu, UriKind.Absolute, out var actual) || !Uri.TryCreate(expected, UriKind.Absolute, out var wanted)) return false;
        return actual.Scheme == wanted.Scheme
               && string.Equals(actual.Host, wanted.Host, StringComparison.OrdinalIgnoreCase)
               && actual.Port == wanted.Port
               && actual.AbsolutePath == wanted.AbsolutePath;
    }

    private static string? StringOf(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DPoPResult Fail(string error) => new(true, null, error);
}
