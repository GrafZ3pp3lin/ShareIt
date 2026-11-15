using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using kroet.ShareIt.Api.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace kroet.ShareIt.Api.Authentication;

/// <summary>The proof was valid.</summary>
public sealed record DpopSuccess;

/// <summary>The proof was rejected with an RFC 9449 error code and description.</summary>
public sealed record DpopFailure(string ErrorCode, string ErrorDescription);

/// <summary>
/// Result of DPoP proof validation. The C# 15 union makes the two outcomes
/// exclusive: a failure always carries an error, a success never does.
/// </summary>
public union DpopValidationResult(DpopSuccess, DpopFailure)
{
    public bool Succeeded => Value is DpopSuccess;

    public string? ErrorCode => Value is DpopFailure failure ? failure.ErrorCode : null;

    public string? ErrorDescription => Value is DpopFailure failure ? failure.ErrorDescription : null;

    public static DpopValidationResult Success() => new DpopSuccess();

    public static DpopValidationResult Failure(string errorCode, string errorDescription) =>
        new DpopFailure(errorCode, errorDescription);
}

/// <summary>
/// Validates DPoP proof JWTs against the access token and the request that
/// carries them (RFC 9449). A DPoP-bound token carries the thumbprint of the
/// client's public key in its <c>cnf.jkt</c> claim; every request using it must
/// include a fresh, single-use proof JWT signed with the matching private key
/// and binding the exact HTTP method and target URI.
/// </summary>
public sealed class DpopProofValidator(
    IOptions<DpopSettings> settings,
    TimeProvider timeProvider,
    ILogger<DpopProofValidator> logger)
{
    private const string ReplayCachePrefix = "dpop:jti:";

    /// <summary>HttpContext.Items key under which the DPoP error code of a failed validation is carried to the challenge event.</summary>
    public const string DpopErrorItemKey = "DpopError";

    // jti → expiry. TryAdd/TryUpdate give atomic single-use enforcement, so
    // concurrent requests presenting the same proof cannot both be accepted.
    private readonly ConcurrentDictionary<string, DateTimeOffset> _replayCache = new();

    public async Task<DpopValidationResult> ValidateAsync(
        string? proof,
        string accessToken,
        string? cnfClaimJson,
        HttpRequest request)
    {
        var jkt = ExtractJkt(cnfClaimJson);

        // Rollout mode: plain bearer tokens (no DPoP binding) stay valid as long
        // as DPoP is not required. Once a token is key-bound, a proof is mandatory.
        if (jkt is null)
        {
            return settings.Value.Required
                ? DpopValidationResult.Failure("missing_token_binding", "A DPoP-bound token is required.")
                : DpopValidationResult.Success();
        }

        if (string.IsNullOrWhiteSpace(proof))
        {
            return DpopValidationResult.Failure("missing_dpop_proof", "A DPoP proof is required for this token.");
        }

        // Parse the proof's JOSE header. Only the raw header/payload are parsed
        // by hand; the signature is verified through the token handler below.
        JsonElement header;
        JsonElement payload;
        try
        {
            var segments = proof.Split('.');
            if (segments.Length != 3 || segments.Any(string.IsNullOrEmpty))
            {
                return DpopValidationResult.Failure("invalid_dpop_proof", "Malformed DPoP proof.");
            }

            header = JsonDocument.Parse(Base64UrlEncoder.Decode(segments[0])).RootElement;
            payload = JsonDocument.Parse(Base64UrlEncoder.Decode(segments[1])).RootElement;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        {
            return DpopValidationResult.Failure("invalid_dpop_proof", "Malformed DPoP proof.");
        }

        if (header.TryGetProperty("typ", out var typ) &&
            !string.Equals(typ.GetString(), "dpop+jwt", StringComparison.OrdinalIgnoreCase))
        {
            return DpopValidationResult.Failure("invalid_dpop_proof", "The DPoP proof must be of type dpop+jwt.");
        }

        var alg = header.TryGetProperty("alg", out var algElement) ? algElement.GetString() : null;
        if (string.IsNullOrEmpty(alg) || alg is "none")
        {
            return DpopValidationResult.Failure("invalid_dpop_proof", "The DPoP proof must be signed.");
        }

        if (!header.TryGetProperty("jwk", out var jwkElement))
        {
            return DpopValidationResult.Failure("invalid_dpop_proof", "The DPoP proof must embed its public key.");
        }

        JsonWebKey proofKey;
        try
        {
            proofKey = JsonWebKey.Create(jwkElement.GetRawText());
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException)
        {
            return DpopValidationResult.Failure("invalid_dpop_proof", "The DPoP proof public key is invalid.");
        }

        // Only asymmetric keys are acceptable; symmetric keys would make the
        // public key in the header verifiable by anyone with the same secret.
        if (proofKey.Kty is not ("EC" or "RSA"))
        {
            return DpopValidationResult.Failure("invalid_dpop_proof", "The DPoP proof key must be asymmetric.");
        }

        // Bind proof → key: the signature must verify against the embedded jwk.
        var signatureResult = await VerifySignatureAsync(proof, proofKey, alg);
        if (!signatureResult.Succeeded)
        {
            return signatureResult;
        }

        // Bind proof → token: ath is the hash of the presented access token.
        if (!payload.TryGetProperty("ath", out var athElement) ||
            !athElement.ValueEquals(ComputeAccessTokenHash(accessToken)))
        {
            return DpopValidationResult.Failure("invalid_dpop_proof",
                "The DPoP proof does not cover this access token.");
        }

        // Bind proof → request: method and canonical target URI must match.
        if (!payload.TryGetProperty("htm", out var htmElement) ||
            !string.Equals(htmElement.GetString(), request.Method, StringComparison.OrdinalIgnoreCase))
        {
            return DpopValidationResult.Failure("invalid_dpop_proof",
                "The DPoP proof was created for another HTTP method.");
        }

        if (!payload.TryGetProperty("htu", out var htuElement) ||
            !string.Equals(NormalizeUri(htuElement.GetString()), NormalizeUri(BuildRequestUri(request)),
                StringComparison.Ordinal))
        {
            return DpopValidationResult.Failure("invalid_dpop_proof", "The DPoP proof was created for another URI.");
        }

        if (!payload.TryGetProperty("iat", out var iatElement) || !iatElement.TryGetInt64(out var iat))
        {
            return DpopValidationResult.Failure("invalid_dpop_proof", "The DPoP proof is missing its creation time.");
        }

        var nowOffset = timeProvider.GetUtcNow();
        var now = nowOffset.ToUnixTimeSeconds();
        if (Math.Abs(now - iat) > settings.Value.ProofMaxAgeSeconds)
        {
            return DpopValidationResult.Failure("invalid_dpop_proof", "The DPoP proof has expired.");
        }

        if (!payload.TryGetProperty("jti", out var jtiElement) || string.IsNullOrEmpty(jtiElement.GetString()))
        {
            return DpopValidationResult.Failure("invalid_dpop_proof", "The DPoP proof is missing its identifier.");
        }

        // Bind key → token: the proof key must be the key the token was bound to.
        var proofJkt = ComputeJwkThumbprint(proofKey);
        if (!string.Equals(proofJkt, jkt, StringComparison.Ordinal))
        {
            return DpopValidationResult.Failure("invalid_token_binding",
                "The DPoP proof key does not match the token binding.");
        }

        // Single-use enforcement: reject a proof whose jti was already accepted.
        if (!TryMarkReplay($"{ReplayCachePrefix}{proofJkt}:{jtiElement.GetString()}", nowOffset))
        {
            return DpopValidationResult.Failure("invalid_dpop_proof", "The DPoP proof was replayed.");
        }

        SweepExpiredReplayEntries(nowOffset);

        return DpopValidationResult.Success();
    }

    /// <summary>
    /// Builds a DPoP <c>WWW-Authenticate</c> challenge header value (RFC 9449
    /// §7.1 / RFC 6750 §3) with optional error information.
    /// </summary>
    public static string BuildChallenge(string errorCode, string? errorDescription)
    {
        var builder = new StringBuilder("DPoP");
        if (!string.IsNullOrEmpty(errorCode))
        {
            builder.Append($", error=\"{EscapeChallengeValue(errorCode)}\"");
        }

        if (!string.IsNullOrEmpty(errorDescription))
        {
            builder.Append($", error_description=\"{EscapeChallengeValue(errorDescription)}\"");
        }

        return builder.ToString();
    }

    private static string EscapeChallengeValue(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>
    /// Atomically marks a proof's <c>jti</c> as seen for the replay window.
    /// Returns false when the jti is still remembered (replay).
    /// </summary>
    private bool TryMarkReplay(string key, DateTimeOffset now)
    {
        var expires = now.AddSeconds(settings.Value.ProofMaxAgeSeconds * 2);
        while (true)
        {
            if (_replayCache.TryGetValue(key, out var existing))
            {
                if (existing > now)
                {
                    return false;
                }

                // Expired entry: overwrite it via atomic compare-and-swap.
                if (_replayCache.TryUpdate(key, expires, existing))
                {
                    return true;
                }

                continue;
            }

            return _replayCache.TryAdd(key, expires);
        }
    }

    private void SweepExpiredReplayEntries(DateTimeOffset now)
    {
        foreach (var entry in _replayCache)
        {
            if (entry.Value <= now)
            {
                _replayCache.TryRemove(KeyValuePair.Create(entry.Key, entry.Value));
            }
        }
    }

    private async Task<DpopValidationResult> VerifySignatureAsync(string proof, JsonWebKey proofKey, string alg)
    {
        try
        {
            var handler = new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler();
            var result = await handler.ValidateTokenAsync(proof, new TokenValidationParameters
            {
                IssuerSigningKey = proofKey,
                ValidAlgorithms = [alg],
                RequireSignedTokens = true,
                RequireExpirationTime = false,
                ValidateLifetime = false,
                ValidateIssuer = false,
                ValidateAudience = false
            });

            return result.IsValid
                ? DpopValidationResult.Success()
                : DpopValidationResult.Failure("invalid_dpop_proof", "The DPoP proof signature is invalid.");
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "DPoP proof signature validation failed");
            return DpopValidationResult.Failure("invalid_dpop_proof", "The DPoP proof signature is invalid.");
        }
    }

    private static string? ExtractJkt(string? cnfClaimJson)
    {
        if (string.IsNullOrEmpty(cnfClaimJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(cnfClaimJson);
            return document.RootElement.TryGetProperty("jkt", out var jkt) ? jkt.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Builds the request's absolute URI (scheme, authority and path; no query
    /// or fragment) as DPoP's <c>htu</c> comparison expects.
    /// </summary>
    private static string BuildRequestUri(HttpRequest request) =>
        $"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}";

    private static string? NormalizeUri(string? value)
    {
        if (string.IsNullOrEmpty(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        // Treat the default ports as absent so htu comparisons survive either
        // spelling (https://host/... and https://host:443/... are the same).
        var isDefaultPort = uri.IsDefaultPort ||
                            (string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) && uri.Port == 443) ||
                            (string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) && uri.Port == 80);

        return isDefaultPort
            ? $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}"
            : $"{uri.Scheme}://{uri.Host}:{uri.Port}{uri.AbsolutePath}";
    }

    private static string ComputeAccessTokenHash(string accessToken) =>
        Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(accessToken)));

    /// <summary>
    /// RFC 7638 JWK thumbprint: SHA-256 over the lexicographically sorted,
    /// whitespace-free JSON of the key's required members.
    /// </summary>
    private static string ComputeJwkThumbprint(JsonWebKey key)
    {
        var canonical = key.Kty switch
        {
            "EC" => $$"""{"crv":"{{key.Crv}}","kty":"{{key.Kty}}","x":"{{key.X}}","y":"{{key.Y}}"}""",
            "RSA" => $$"""{"e":"{{key.E}}","kty":"{{key.Kty}}","n":"{{key.N}}"}""",
            _ => throw new InvalidOperationException("Unsupported JWK key type.")
        };

        return Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
