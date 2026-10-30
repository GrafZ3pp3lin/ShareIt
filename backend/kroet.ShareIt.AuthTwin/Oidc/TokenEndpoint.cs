using System.Security.Cryptography;
using System.Text;
using kroet.ShareIt.AuthTwin.Hosting;
using kroet.ShareIt.AuthTwin.Realm;
using kroet.ShareIt.AuthTwin.Store;
using Microsoft.AspNetCore.WebUtilities;
using static kroet.ShareIt.AuthTwin.Oidc.ClientRequests;

namespace kroet.ShareIt.AuthTwin.Oidc;

/// <summary>
/// The token endpoint, checking in Keycloak's order: grant type, client, origin, DPoP proof, then the grant.
/// So a missing proof is refused before the code is looked at, and doesn't use it up.
/// </summary>
internal static class TokenEndpoint
{
    private static readonly HashSet<string> KnownGrants = ["authorization_code", "refresh_token"];

    public static void MapToken(this IEndpointRouteBuilder app)
    {
        var realm = app.MapGroup("/realms/{realm}/protocol/openid-connect/token").AddEndpointFilter(DiscoveryEndpoints.RealmExists);
        realm.MapPost("", Token);
        realm.MapMethods("", ["OPTIONS"], (HttpContext context) => Cors.Preflight(context, "POST, OPTIONS"));
        realm.MapGet("", MethodNotAllowed);
    }

    private static async Task<IResult> Token(
        HttpContext context, RealmModel realm, UserStore users, AuthStore store, TokenFactory tokens, TwinUrls urls, SigningKey key, TimeProvider time)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        var form = await ReadFormAsync(context.Request);

        var grantType = Value(form["grant_type"]);
        if (grantType is null) return Error("invalid_request", "Missing form parameter: grant_type");
        if (!KnownGrants.Contains(grantType)) return Error("unsupported_grant_type", "Unsupported grant_type");

        var client = Authenticate(form, realm, out var clientError);
        if (client is null) return clientError!;
        if (CheckOrigin(context, client) is { } originError) return originError;

        var proof = DPoPProof.Validate(context.Request, urls.OidcEndpoint("token"), time, store);
        if (proof.Error is not null) return Error("invalid_request", proof.Error);
        if (!proof.Present && client.DpopBoundAccessTokens) return Error("invalid_request", "DPoP proof is missing");

        return grantType switch
        {
            "authorization_code" => AuthorizationCode(form, client, users, store, tokens, proof),
            "refresh_token" => Refresh(form, client, users, store, tokens, urls, key, proof, time),
            _ => Error("unsupported_grant_type", "Unsupported grant_type"),
        };
    }

    private static IResult AuthorizationCode(
        IFormCollection form, ClientModel client, UserStore users, AuthStore store, TokenFactory tokens, DPoPResult proof)
    {
        var code = Value(form["code"]);
        if (code is null) return Error("invalid_request", "Missing parameter: code");

        var authCode = store.TakeCode(code);
        var user = authCode is null ? null : users.Find(authCode.UserId);
        var session = authCode is null ? null : store.RefreshSession(authCode.SessionId);
        if (authCode is null || user is null || !user.Enabled || session is null) return Error("invalid_grant", "Code not valid");

        var request = authCode.Request;
        if (request.ClientId != client.ClientId) return Error("invalid_grant", "Auth error: Found different client_id in clientSession");
        if (request.RedirectUri != Value(form["redirect_uri"])) return Error("invalid_grant", "Incorrect redirect_uri");

        if (request.CodeChallenge is not null)
        {
            var verifier = Value(form["code_verifier"]);
            if (verifier is null || verifier.Length is < 43 or > 128)
                return Error("invalid_grant", "PKCE verification failed: Invalid code verifier");
            var hash = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            if (hash != request.CodeChallenge) return Error("invalid_grant", "PKCE verification failed: Code mismatch");
        }

        if (request.DpopJkt is not null && request.DpopJkt != proof.Thumbprint)
            return Error("invalid_request", "DPoP Proof public key thumbprint does not match dpop_jkt");

        return Results.Json(tokens.UserTokens(new UserGrant(user, client, request.Scopes, session, request.Nonce, proof.Thumbprint)));
    }

    /// <summary>No rotation: the presented refresh token stays valid, so tabs refreshing at once all succeed.</summary>
    private static IResult Refresh(
        IFormCollection form, ClientModel client, UserStore users, AuthStore store, TokenFactory tokens, TwinUrls urls, SigningKey key,
        DPoPResult proof, TimeProvider time)
    {
        var refreshToken = Value(form["refresh_token"]);
        if (refreshToken is null) return Error("invalid_request", "No refresh token");

        var claims = Jwt.Read(refreshToken, key);
        if (claims is null) return Error("invalid_grant", "Invalid refresh token");
        var token = claims.Value;
        if (token.String("typ") != "Refresh" || token.String("iss") != urls.Issuer)
            return Error("invalid_grant", "Invalid refresh token");
        if (token.String("azp") != client.ClientId)
            return Error("invalid_grant", "Invalid refresh token. Token client and authorized client don't match");

        var boundTo = token.TryGetProperty("cnf", out var cnf) && cnf.TryGetProperty("jkt", out var jkt) ? jkt.GetString() : null;
        if (boundTo is not null && !proof.Present) return Error("invalid_grant", "DPoP proof is missing");
        if (boundTo is not null && boundTo != proof.Thumbprint) return Error("invalid_grant", "DPoP confirmation doesn't match DPoP proof");

        var sessionId = token.String("sid") ?? "";
        if (store.RefreshSession(sessionId, touch: false) is null) return Error("invalid_grant", "Session not active");
        if (token.TryGetProperty("exp", out var exp) && exp.GetInt64() < time.GetUtcNow().ToUnixTimeSeconds())
            return Error("invalid_grant", "Token is not active");
        var user = users.Find(token.String("sub") ?? "");
        if (user is null || !user.Enabled) return Error("invalid_grant", "Invalid refresh token");

        // Keycloak checks a scope parameter but keeps the refresh token's scopes either way.
        var requested = Value(form["scope"]);
        if (requested is not null && !TokenFactory.AllowsScopes(client, requested))
            return Error("invalid_scope", $"Invalid scopes: {requested}");

        // Only now, so a refused refresh doesn't reset the session's idle time.
        var session = store.RefreshSession(sessionId);
        if (session is null) return Error("invalid_grant", "Session not active");

        var scopes = (token.String("scope") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Results.Json(tokens.UserTokens(new UserGrant(user, client, scopes, session, null, proof.Thumbprint)));
    }
}
