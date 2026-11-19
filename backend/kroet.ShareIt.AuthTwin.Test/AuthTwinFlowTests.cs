using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace kroet.ShareIt.AuthTwin.Test;

[TestClass]
public sealed partial class AuthTwinFlowTests
{
    private const string ClientId = "shareit-spa-dev";
    private const string RedirectUri = "http://localhost:3001/auth/callback";
    private const string PostLogoutRedirectUri = "http://localhost:3001";
    private const string Audience = "shareit-api-dev";
    private const string DevUser = "dev@shareit.local";

    [TestMethod]
    public async Task Discovery_AdvertisesOnlyTheEndpointsTheAppUses()
    {
        await using var host = await AuthTwinHost.StartAsync();
        using var client = CreateClient(host);

        using var discovery = await GetJsonAsync(client, $"{host.Issuer}/.well-known/openid-configuration");
        var root = discovery.RootElement;
        Assert.AreEqual(host.Issuer, root.GetProperty("issuer").GetString());
        Assert.AreEqual($"{host.Issuer}/protocol/openid-connect/auth", root.GetProperty("authorization_endpoint").GetString());
        Assert.AreEqual($"{host.Issuer}/protocol/openid-connect/token", root.GetProperty("token_endpoint").GetString());
        Assert.AreEqual($"{host.Issuer}/protocol/openid-connect/logout", root.GetProperty("end_session_endpoint").GetString());
        Assert.AreEqual($"{host.Issuer}/protocol/openid-connect/certs", root.GetProperty("jwks_uri").GetString());
        Assert.Contains(v => v.GetString() == "S256", root.GetProperty("code_challenge_methods_supported").EnumerateArray());
        Assert.Contains(v => v.GetString() == "ES256", root.GetProperty("dpop_signing_alg_values_supported").EnumerateArray());

        using var jwks = await GetJsonAsync(client, root.GetProperty("jwks_uri").GetString()!);
        var key = jwks.RootElement.GetProperty("keys").EnumerateArray().Single();
        Assert.AreEqual("RSA", key.GetProperty("kty").GetString());
        Assert.AreEqual("RS256", key.GetProperty("alg").GetString());
    }

    [TestMethod]
    public async Task AuthorizationCodeFlow_IssuesVerifiableDpopBoundTokens()
    {
        await using var host = await AuthTwinHost.StartAsync();
        using var client = CreateClient(host);
        using var key = new DpopKey();
        var (verifier, challenge) = CreatePkce();
        const string nonce = "nonce-for-the-flow-test";

        var code = await AuthorizeAsync(client, host.Issuer, key.Jkt, challenge, nonce);
        var (status, tokens) = await ExchangeCodeAsync(client, host.Issuer, code, verifier, key.Proof(TokenEndpoint(host.Issuer), "POST"));

        Assert.AreEqual(HttpStatusCode.OK, status);
        Assert.AreEqual("DPoP", tokens.GetProperty("token_type").GetString());
        var accessToken = tokens.GetProperty("access_token").GetString()!;
        var idToken = tokens.GetProperty("id_token").GetString()!;
        Assert.IsFalse(string.IsNullOrEmpty(tokens.GetProperty("refresh_token").GetString()));

        var claims = Payload(accessToken);
        Assert.AreEqual(host.Issuer, claims.GetProperty("iss").GetString());
        Assert.AreEqual(Audience, claims.GetProperty("aud").GetString());
        Assert.AreEqual("DPoP", claims.GetProperty("typ").GetString());
        Assert.AreEqual(key.Jkt, claims.GetProperty("cnf").GetProperty("jkt").GetString());
        Assert.IsTrue(Guid.TryParse(claims.GetProperty("sub").GetString(), out _));
        Assert.Contains("openid", claims.GetProperty("scope").GetString()!, StringComparison.Ordinal);
        Assert.Contains("profile", claims.GetProperty("scope").GetString()!, StringComparison.Ordinal);

        var keys = await GetSigningKeysAsync(client, host.Issuer);
        var handler = new JsonWebTokenHandler();
        var accessValidation = await handler.ValidateTokenAsync(accessToken, new TokenValidationParameters
        {
            ValidIssuer = host.Issuer,
            ValidAudience = Audience,
            IssuerSigningKeys = keys,
        });
        Assert.IsTrue(accessValidation.IsValid, accessValidation.Exception?.Message);

        var idValidation = await handler.ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidIssuer = host.Issuer,
            ValidAudience = ClientId,
            IssuerSigningKeys = keys,
        });
        Assert.IsTrue(idValidation.IsValid, idValidation.Exception?.Message);
        Assert.AreEqual(nonce, Payload(idToken).GetProperty("nonce").GetString());
    }

    [TestMethod]
    public async Task TokenEndpoint_RefusesMissingOrMismatchedDpopProofs()
    {
        await using var host = await AuthTwinHost.StartAsync();
        using var client = CreateClient(host);
        using var boundKey = new DpopKey();
        using var otherKey = new DpopKey();
        var (verifier, challenge) = CreatePkce();

        var code = await AuthorizeAsync(client, host.Issuer, boundKey.Jkt, challenge, nonce: null);

        var (missingStatus, missing) = await ExchangeCodeAsync(client, host.Issuer, code, verifier, proof: null);
        Assert.AreEqual(HttpStatusCode.BadRequest, missingStatus);
        Assert.AreEqual("invalid_request", missing.GetProperty("error").GetString());
        Assert.AreEqual("DPoP proof is missing", missing.GetProperty("error_description").GetString());

        // The missing proof was refused before the grant, so the code still works.
        var (okStatus, ok) = await ExchangeCodeAsync(
            client, host.Issuer, code, verifier, boundKey.Proof(TokenEndpoint(host.Issuer), "POST"));
        Assert.AreEqual(HttpStatusCode.OK, okStatus);
        Assert.AreEqual("DPoP", ok.GetProperty("token_type").GetString());

        var secondCode = await AuthorizeAsync(client, host.Issuer, boundKey.Jkt, challenge, nonce: null);
        var (wrongStatus, wrong) = await ExchangeCodeAsync(
            client, host.Issuer, secondCode, verifier, otherKey.Proof(TokenEndpoint(host.Issuer), "POST"));
        Assert.AreEqual(HttpStatusCode.BadRequest, wrongStatus);
        Assert.AreEqual("invalid_request", wrong.GetProperty("error").GetString());
        Assert.Contains("does not match dpop_jkt", wrong.GetProperty("error_description").GetString()!, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task RefreshToken_IssuesFreshTokensAndLogoutEndsTheSession()
    {
        await using var host = await AuthTwinHost.StartAsync();
        using var client = CreateClient(host);
        using var key = new DpopKey();
        var (verifier, challenge) = CreatePkce();

        var code = await AuthorizeAsync(client, host.Issuer, key.Jkt, challenge, nonce: null);
        var (_, tokens) = await ExchangeCodeAsync(client, host.Issuer, code, verifier, key.Proof(TokenEndpoint(host.Issuer), "POST"));
        var accessToken = tokens.GetProperty("access_token").GetString()!;
        var refreshToken = tokens.GetProperty("refresh_token").GetString()!;

        var (refreshStatus, refreshed) = await RefreshAsync(
            client, host.Issuer, refreshToken, key.Proof(TokenEndpoint(host.Issuer), "POST"));
        Assert.AreEqual(HttpStatusCode.OK, refreshStatus);
        var newAccessToken = refreshed.GetProperty("access_token").GetString()!;
        Assert.AreNotEqual(accessToken, newAccessToken);
        Assert.AreEqual(
            Payload(accessToken).GetProperty("sub").GetString(),
            Payload(newAccessToken).GetProperty("sub").GetString());

        var (missingStatus, missing) = await RefreshAsync(client, host.Issuer, refreshToken, proof: null);
        Assert.AreEqual(HttpStatusCode.BadRequest, missingStatus);
        Assert.AreEqual("invalid_request", missing.GetProperty("error").GetString());

        var logoutUrl = QueryHelpers.AddQueryString($"{host.Issuer}/protocol/openid-connect/logout", new Dictionary<string, string?>
        {
            ["id_token_hint"] = tokens.GetProperty("id_token").GetString(),
            ["post_logout_redirect_uri"] = PostLogoutRedirectUri,
            ["state"] = "logged-out",
        }.Where(p => p.Value is not null));
        using var logout = await client.GetAsync(logoutUrl);
        Assert.AreEqual(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.AreEqual($"{PostLogoutRedirectUri}/?state=logged-out", logout.Headers.Location?.ToString());

        var (status, body) = await RefreshAsync(client, host.Issuer, refreshToken, key.Proof(TokenEndpoint(host.Issuer), "POST"));
        Assert.AreEqual(HttpStatusCode.BadRequest, status);
        Assert.AreEqual("Session not active", body.GetProperty("error_description").GetString());
    }

    [TestMethod]
    public async Task ControlPlane_IssuesTokensWithoutABrowserAndEndsSessions()
    {
        await using var host = await AuthTwinHost.StartAsync();
        using var client = CreateClient(host);
        using var key = new DpopKey();

        using var users = await GetJsonAsync(client, "/control/users");
        var user = users.RootElement.EnumerateArray().Single(u => u.GetProperty("email").GetString() == DevUser);
        var userId = user.GetProperty("id").GetString();

        var request = new
        {
            clientId = ClientId,
            jkt = key.Jkt,
        };
        using var response = await client.PostAsJsonAsync($"/control/users/{userId}/tokens", request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var tokens = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var accessToken = tokens.RootElement.GetProperty("access_token").GetString()!;
        Assert.AreEqual(key.Jkt, Payload(accessToken).GetProperty("cnf").GetProperty("jkt").GetString());

        using var revoke = await client.PostAsync($"/control/users/{userId}/revoke-sessions", null);
        Assert.AreEqual(HttpStatusCode.OK, revoke.StatusCode);
        using var revokeBody = JsonDocument.Parse(await revoke.Content.ReadAsStringAsync());
        Assert.AreEqual(1, revokeBody.RootElement.GetProperty("ended").GetInt32());

        var refreshToken = tokens.RootElement.GetProperty("refresh_token").GetString()!;
        var (status, body) = await RefreshAsync(client, host.Issuer, refreshToken, key.Proof(TokenEndpoint(host.Issuer), "POST"));
        Assert.AreEqual(HttpStatusCode.BadRequest, status);
        Assert.AreEqual("Session not active", body.GetProperty("error_description").GetString());
    }

    private static HttpClient CreateClient(AuthTwinHost host) =>
        new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = host.BaseUrl };

    private static string TokenEndpoint(string issuer) => $"{issuer}/protocol/openid-connect/token";

    [GeneratedRegex("id=\"kc-form-login\"[^>]*action=\"([^\"]+)\"")]
    private static partial Regex LoginFormActionRegex();

    private static async Task<string> AuthorizeAsync(
        HttpClient client, string issuer, string dpopJkt, string codeChallenge, string? nonce, string state = "test-state")
    {
        var authUrl = QueryHelpers.AddQueryString($"{issuer}/protocol/openid-connect/auth", new Dictionary<string, string?>
        {
            ["client_id"] = ClientId,
            ["redirect_uri"] = RedirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid profile",
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["dpop_jkt"] = dpopJkt,
        }.Where(p => p.Value is not null));

        using var page = await client.GetAsync(authUrl);
        Assert.AreEqual(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        var action = WebUtility.HtmlDecode(
            LoginFormActionRegex().Match(html).Groups[1].Value);
        Assert.IsFalse(string.IsNullOrEmpty(action), "The login form action was not found.");

        using var login = await client.PostAsync(action, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = DevUser,
        }));
        Assert.AreEqual(HttpStatusCode.Redirect, login.StatusCode);
        var location = login.Headers.Location ?? throw new AssertFailedException("The login response had no redirect.");
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.AreEqual(state, query["state"].ToString());
        Assert.AreEqual(issuer, query["iss"].ToString());
        Assert.IsFalse(string.IsNullOrEmpty(query["code"].ToString()));
        return query["code"].ToString();
    }

    private static Task<(HttpStatusCode Status, JsonElement Body)> ExchangeCodeAsync(
        HttpClient client, string issuer, string code, string verifier, string? proof) =>
        PostFormAsync(client, TokenEndpoint(issuer), new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = ClientId,
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["code_verifier"] = verifier,
        }, proof);

    private static Task<(HttpStatusCode Status, JsonElement Body)> RefreshAsync(
        HttpClient client, string issuer, string refreshToken, string? proof) =>
        PostFormAsync(client, TokenEndpoint(issuer), new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = ClientId,
            ["refresh_token"] = refreshToken,
            ["scope"] = "openid profile",
        }, proof);

    private static async Task<(HttpStatusCode Status, JsonElement Body)> PostFormAsync(
        HttpClient client, string url, Dictionary<string, string> form, string? proof)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(form),
        };
        if (proof is not null) request.Headers.TryAddWithoutValidation("DPoP", proof);

        using var response = await client.SendAsync(request);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response.StatusCode, document.RootElement.Clone());
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static JsonElement Payload(string token) =>
        JsonSerializer.Deserialize<JsonElement>(Base64UrlEncoder.Decode(token.Split('.')[1]));

    private static async Task<IList<SecurityKey>> GetSigningKeysAsync(HttpClient client, string issuer)
    {
        using var jwks = await GetJsonAsync(client, $"{issuer}/protocol/openid-connect/certs");
        return new JsonWebKeySet(jwks.RootElement.GetRawText()).GetSigningKeys();
    }

    private static (string Verifier, string Challenge) CreatePkce()
    {
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }
}
