using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using kroet.ShareIt.Api.Authentication;
using kroet.ShareIt.Api.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace kroet.ShareIt.Api.Test.Authentication;

/// <summary>
/// Unit tests for the DPoP proof validation (RFC 9449) performed on every
/// request that presents a DPoP-bound access token.
/// </summary>
[TestClass]
public sealed class DpopProofValidatorTests
{
    private const string AccessToken = "test-access-token";
    private const string TargetUri = "https://api.example.test/api/v1/content/package/1/upload";

    private static DpopProofValidator CreateValidator(DpopSettings? settings = null) =>
        new(
            Options.Create(settings ?? new DpopSettings()),
            TimeProvider.System);

    private static HttpRequest CreateRequest(string method = "POST") =>
        new DefaultHttpContext
        {
            Request =
            {
                Scheme = "https",
                Host = new HostString("api.example.test"),
                Method = method,
                Path = "/api/v1/content/package/1/upload"
            }
        }.Request;

    private static (ECDsa Key, string PublicJwkJson) CreateKey()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(false);
        var jwk = new
        {
            kty = "EC",
            crv = "P-256",
            x = Base64UrlEncoder.Encode(parameters.Q.X),
            y = Base64UrlEncoder.Encode(parameters.Q.Y)
        };
        return (key, JsonSerializer.Serialize(jwk));
    }

    private static string JwkThumbprint(string publicJwkJson)
    {
        using var document = JsonDocument.Parse(publicJwkJson);
        var root = document.RootElement;
        var canonical = $$"""
            {"crv":"{{root.GetProperty("crv").GetString()}}","kty":"{{root.GetProperty("kty").GetString()}}","x":"{{root.GetProperty("x").GetString()}}","y":"{{root.GetProperty("y").GetString()}}"}
            """;
        return Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string BuildProof(
        ECDsa key,
        string publicJwkJson,
        object payload,
        string alg = "ES256",
        string typ = "dpop+jwt")
    {
        var encode = (object value) => Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(value));
        // Real DPoP proofs embed the public key as a JWK object (not a string).
        var header = new Dictionary<string, object>
        {
            ["alg"] = alg,
            ["typ"] = typ,
            ["jwk"] = JsonSerializer.Deserialize<JsonElement>(publicJwkJson)
        };
        var signingInput = $"{encode(header)}.{encode(payload)}";
        var signature = Base64UrlEncoder.Encode(
            key.SignData(Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256));
        return $"{signingInput}.{signature}";
    }

    private static object BuildPayload(
        string method = "POST",
        string uri = TargetUri,
        string? accessToken = AccessToken,
        string? jti = null,
        long? iat = null)
    {
        return new
        {
            jti = jti ?? Guid.NewGuid().ToString(),
            htm = method,
            htu = uri,
            iat = iat ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ath = accessToken is null
                ? null
                : Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(accessToken)))
        };
    }

    private static string CnfClaim(string jkt) => $$"""{"jkt":"{{jkt}}"}""";

    private Task<DpopValidationResult> ValidateAsync(
        DpopProofValidator validator,
        string? proof,
        string? cnfJson,
        HttpRequest? request = null) =>
        validator.ValidateAsync(proof, AccessToken, cnfJson, request ?? CreateRequest());

    [TestMethod]
    public void BuildsAChallengeWithEscapedErrorInformation()
    {
        var challenge = DpopProofValidator.BuildChallenge("invalid_dpop_proof", "The \"proof\" was replayed.\\");

        Assert.AreEqual(
            "DPoP, error=\"invalid_dpop_proof\", error_description=\"The \\\"proof\\\" was replayed.\\\\\"",
            challenge);
    }

    [TestMethod]
    public async Task AcceptsAValidProof()
    {
        var (key, jwk) = CreateKey();
        var jkt = JwkThumbprint(jwk);
        var validator = CreateValidator();

        var result = await ValidateAsync(validator, BuildProof(key, jwk, BuildPayload()), CnfClaim(jkt));

        Assert.IsTrue(result.Succeeded, result.ErrorDescription);
    }

    [TestMethod]
    public async Task AcceptsAHtuWithTheDefaultPortSpelledOut()
    {
        var (key, jwk) = CreateKey();
        var jkt = JwkThumbprint(jwk);
        var validator = CreateValidator();

        var result = await ValidateAsync(
            validator, BuildProof(key, jwk, BuildPayload(uri: "https://api.example.test:443/api/v1/content/package/1/upload")),
            CnfClaim(jkt));

        Assert.IsTrue(result.Succeeded, result.ErrorDescription);
    }

    [TestMethod]
    public async Task RejectsAMissingProof()
    {
        var jkt = JwkThumbprint(CreateKey().PublicJwkJson);

        var result = await ValidateAsync(CreateValidator(), null, CnfClaim(jkt));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("missing_dpop_proof", result.ErrorCode);
    }

    [TestMethod]
    public async Task RejectsAMalformedProof()
    {
        var jkt = JwkThumbprint(CreateKey().PublicJwkJson);

        var result = await ValidateAsync(CreateValidator(), "not-a-jwt", CnfClaim(jkt));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("invalid_dpop_proof", result.ErrorCode);
    }

    [TestMethod]
    public async Task RejectsAnotherHttpMethod()
    {
        var (key, jwk) = CreateKey();
        var jkt = JwkThumbprint(jwk);

        var result = await ValidateAsync(
            CreateValidator(), BuildProof(key, jwk, BuildPayload(method: "GET")), CnfClaim(jkt));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("invalid_dpop_proof", result.ErrorCode);
    }

    [TestMethod]
    public async Task RejectsAnotherUri()
    {
        var (key, jwk) = CreateKey();
        var jkt = JwkThumbprint(jwk);

        var result = await ValidateAsync(
            CreateValidator(), BuildProof(key, jwk, BuildPayload(uri: "https://other.example.test/api/v1/x")),
            CnfClaim(jkt));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("invalid_dpop_proof", result.ErrorCode);
    }

    [TestMethod]
    public async Task RejectsAStaleProof()
    {
        var (key, jwk) = CreateKey();
        var jkt = JwkThumbprint(jwk);
        var stale = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 301;

        var result = await ValidateAsync(
            CreateValidator(), BuildProof(key, jwk, BuildPayload(iat: stale)), CnfClaim(jkt));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("invalid_dpop_proof", result.ErrorCode);
    }

    [TestMethod]
    public async Task RejectsAProofHashingAnotherAccessToken()
    {
        var (key, jwk) = CreateKey();
        var jkt = JwkThumbprint(jwk);

        var result = await ValidateAsync(
            CreateValidator(), BuildProof(key, jwk, BuildPayload(accessToken: "another-token")), CnfClaim(jkt));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("invalid_dpop_proof", result.ErrorCode);
    }

    [TestMethod]
    public async Task RejectsAProofSignedByAnotherKey()
    {
        var (_, jwk) = CreateKey();
        var (otherKey, _) = CreateKey();
        var jkt = JwkThumbprint(jwk);

        var result = await ValidateAsync(
            CreateValidator(), BuildProof(otherKey, jwk, BuildPayload()), CnfClaim(jkt));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("invalid_dpop_proof", result.ErrorCode);
    }

    [TestMethod]
    public async Task RejectsAProofKeyThatDoesNotMatchTheTokenBinding()
    {
        // Proof and signature are consistent, but the token is bound to a
        // different key than the one proving possession.
        var (proofKey, proofJwk) = CreateKey();
        var (boundKey, boundJwk) = CreateKey();
        var proof = BuildProof(proofKey, boundJwk, BuildPayload());
        var proofJkt = JwkThumbprint(proofJwk);
        var boundJkt = JwkThumbprint(boundJwk);

        // The signature must verify against the proof's own embedded key, so the
        // proof embeds the bound key while being signed by the other key: the
        // signature check fails first; the thumbprint mismatch case is covered
        // by binding the proof to the wrong (bound) key below.
        var wrongSignature = await ValidateAsync(CreateValidator(), proof, CnfClaim(proofJkt));
        Assert.IsFalse(wrongSignature.Succeeded);

        var wrongBinding = await ValidateAsync(
            CreateValidator(), BuildProof(boundKey, boundJwk, BuildPayload()), CnfClaim(proofJkt));
        Assert.AreEqual("invalid_token_binding", wrongBinding.ErrorCode);
        Assert.AreNotEqual(proofJkt, boundJkt);
    }

    [TestMethod]
    public async Task RejectsAReplayedProof()
    {
        var (key, jwk) = CreateKey();
        var jkt = JwkThumbprint(jwk);
        var validator = CreateValidator();
        var proof = BuildProof(key, jwk, BuildPayload(jti: "replayed-jti"));

        var first = await ValidateAsync(validator, proof, CnfClaim(jkt));
        var second = await ValidateAsync(validator, proof, CnfClaim(jkt));

        Assert.IsTrue(first.Succeeded, first.ErrorDescription);
        Assert.IsFalse(second.Succeeded);
        Assert.AreEqual("invalid_dpop_proof", second.ErrorCode);
    }

    [TestMethod]
    public async Task AcceptsAConcurrentlyReplayedProofOnlyOnce()
    {
        // TryAdd-based single-use enforcement must be atomic: concurrent
        // requests presenting the same proof cannot all be accepted.
        var (key, jwk) = CreateKey();
        var jkt = JwkThumbprint(jwk);
        var validator = CreateValidator();
        var proof = BuildProof(key, jwk, BuildPayload(jti: "raced-jti"));

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => ValidateAsync(validator, proof, CnfClaim(jkt))));

        Assert.AreEqual(1, results.Count(r => r.Succeeded));
        Assert.IsTrue(results.All(r => r.Succeeded || r.ErrorCode == "invalid_dpop_proof"));
    }

    [TestMethod]
    public async Task RejectsAnUnsignedProof()
    {
        var (_, jwk) = CreateKey();
        var jkt = JwkThumbprint(jwk);

        // alg none: the JOSE header advertises no signature at all.
        var header = $$"""{"alg":"none","typ":"dpop+jwt","jwk":{{jwk}}}""";
        var payload = Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(BuildPayload()));
        var proof = $"{Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(header))}.{payload}.";

        var result = await ValidateAsync(CreateValidator(), proof, CnfClaim(jkt));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("invalid_dpop_proof", result.ErrorCode);
    }

    [TestMethod]
    public async Task RejectsASymmetricProofKey()
    {
        var (_, jwk) = CreateKey();
        var jkt = JwkThumbprint(jwk);
        var octJwk = $$"""{"kty":"oct","k":"{{Base64UrlEncoder.Encode("symmetric-secret")}}"}""";
        var proof = BuildProof(ECDsa.Create(), octJwk, BuildPayload(), alg: "HS256");

        var result = await ValidateAsync(CreateValidator(), proof, CnfClaim(jkt));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("invalid_dpop_proof", result.ErrorCode);
    }

    [TestMethod]
    public async Task PlainBearerTokensPassWhenDpopIsNotRequired()
    {
        var result = await ValidateAsync(CreateValidator(new DpopSettings { Required = false }), null, cnfJson: null);

        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public async Task PlainBearerTokensAreRejectedWhenDpopIsRequired()
    {
        var result = await ValidateAsync(CreateValidator(new DpopSettings { Required = true }), null, cnfJson: null);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("missing_token_binding", result.ErrorCode);
    }

    [TestMethod]
    public async Task DpopBoundTokensStillRequireProofsWhenDpopIsRequired()
    {
        var (key, jwk) = CreateKey();
        var jkt = JwkThumbprint(jwk);
        var validator = CreateValidator(new DpopSettings { Required = true });

        var withoutProof = await ValidateAsync(validator, null, CnfClaim(jkt));
        var withProof = await ValidateAsync(validator, BuildProof(key, jwk, BuildPayload()), CnfClaim(jkt));

        Assert.IsFalse(withoutProof.Succeeded);
        Assert.AreEqual("missing_dpop_proof", withoutProof.ErrorCode);
        Assert.IsTrue(withProof.Succeeded, withProof.ErrorDescription);
    }
}
