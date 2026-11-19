using kroet.ShareIt.AuthTwin.Hosting;
using kroet.ShareIt.AuthTwin.Realm;
using kroet.ShareIt.AuthTwin.Store;

namespace kroet.ShareIt.AuthTwin.Oidc;

internal sealed record UserGrant(
    TwinUser User,
    ClientModel Client,
    IReadOnlyList<string> RequestedScopes,
    UserSession Session,
    string? Nonce,
    string? Jkt);

/// <summary>Builds tokens with the claims Keycloak puts in them for the realm's public client.</summary>
internal sealed class TokenFactory(RealmModel realm, SigningKey key, TwinUrls urls, TimeProvider time)
{
    // Scopes that show up in the token's scope claim; the others only add claims.
    private static readonly string[] VisibleScopes = ["openid", "profile", "email"];

    /// <summary>Every requested scope is openid or one of the client's default or optional scopes.</summary>
    public static bool AllowsScopes(ClientModel client, string scope) =>
        scope.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(s => s == "openid" || client.DefaultScopes.Contains(s) || client.OptionalScopes.Contains(s));

    /// <summary>openid if asked, every default scope, and the optional scopes that were asked for.</summary>
    public static IReadOnlyList<string> Grant(ClientModel client, IReadOnlyList<string> requested) =>
        [.. requested.Where(s => s == "openid")
            .Concat(client.DefaultScopes)
            .Concat(requested.Where(client.OptionalScopes.Contains))
            .Distinct()];

    /// <param name="issuedAt">Control plane only: backdates the tokens, e.g. for an already expired access token.</param>
    /// <param name="accessLifespan">Control plane only: overrides the realm's access token lifespan.</param>
    public Dictionary<string, object?> UserTokens(UserGrant grant, DateTimeOffset? issuedAt = null, TimeSpan? accessLifespan = null)
    {
        var scopes = Grant(grant.Client, grant.RequestedScopes);
        var now = issuedAt ?? time.GetUtcNow();
        var lifespan = accessLifespan ?? realm.AccessTokenLifespan;
        var scope = VisibleScope(scopes);
        var refreshExpiresAt = Min(now + realm.SsoSessionIdleTimeout, grant.Session.Started + realm.SsoSessionMaxLifespan);

        var accessToken = Jwt.Sign(AccessClaims(grant, scopes, scope, now, lifespan), key);
        var response = new Dictionary<string, object?>
        {
            ["access_token"] = accessToken,
            ["expires_in"] = (long)lifespan.TotalSeconds,
            ["refresh_expires_in"] = (long)(refreshExpiresAt - now).TotalSeconds,
            ["refresh_token"] = Jwt.Sign(RefreshClaims(grant, scopes, now, refreshExpiresAt), key),
            ["token_type"] = TokenType(grant.Jkt),
        };
        if (scopes.Contains("openid")) response["id_token"] = Jwt.Sign(IdClaims(grant, scopes, accessToken, now, lifespan), key);
        response["not-before-policy"] = 0;
        response["session_state"] = grant.Session.Id;
        response["scope"] = scope;
        return response;
    }

    private Dictionary<string, object?> AccessClaims(
        UserGrant grant, IReadOnlyList<string> scopes, string scope, DateTimeOffset now, TimeSpan lifespan)
    {
        var (user, client) = (grant.User, grant.Client);
        var claims = new Dictionary<string, object?>
        {
            ["exp"] = (now + lifespan).ToUnixTimeSeconds(),
            ["iat"] = now.ToUnixTimeSeconds(),
            ["auth_time"] = grant.Session.AuthTime.ToUnixTimeSeconds(),
            ["jti"] = $"onrtac:{Guid.NewGuid()}",
            ["iss"] = urls.Issuer,
            ["aud"] = client.Audience ?? client.ClientId,
            ["sub"] = user.Id,
            ["typ"] = TokenType(grant.Jkt),
            ["azp"] = client.ClientId,
            ["sid"] = grant.Session.Id,
            ["acr"] = scopes.Contains("acr") ? "1" : null,
        };

        if (scopes.Contains("web-origins") && ClientUris.WebOrigins(client) is { Count: > 0 } origins)
            claims["allowed-origins"] = origins;

        if (scopes.Contains("roles"))
        {
            // A default Keycloak realm gives every user these realm roles.
            claims["realm_access"] = new { roles = new[] { "offline_access", $"default-roles-{realm.Name}", "uma_authorization" } };
        }

        claims["cnf"] = Confirmation(grant.Jkt);
        claims["scope"] = scope;
        AddUserClaims(claims, user, scopes);
        return claims;
    }

    private Dictionary<string, object?> IdClaims(
        UserGrant grant, IReadOnlyList<string> scopes, string accessToken, DateTimeOffset now, TimeSpan lifespan)
    {
        var claims = new Dictionary<string, object?>
        {
            ["exp"] = (now + lifespan).ToUnixTimeSeconds(),
            ["iat"] = now.ToUnixTimeSeconds(),
            ["auth_time"] = grant.Session.AuthTime.ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString(),
            ["iss"] = urls.Issuer,
            ["aud"] = grant.Client.ClientId,
            ["sub"] = grant.User.Id,
            ["typ"] = "ID",
            ["azp"] = grant.Client.ClientId,
            ["nonce"] = grant.Nonce,
            ["sid"] = grant.Session.Id,
            ["at_hash"] = Jwt.AtHash(accessToken),
            ["acr"] = scopes.Contains("acr") ? "1" : null,
        };
        AddUserClaims(claims, grant.User, scopes);
        return claims;
    }

    private Dictionary<string, object?> RefreshClaims(
        UserGrant grant, IReadOnlyList<string> scopes, DateTimeOffset now, DateTimeOffset expiresAt) => new()
    {
        ["exp"] = expiresAt.ToUnixTimeSeconds(),
        ["iat"] = now.ToUnixTimeSeconds(),
        ["jti"] = Guid.NewGuid().ToString(),
        ["iss"] = urls.Issuer,
        ["aud"] = urls.Issuer,
        ["sub"] = grant.User.Id,
        ["typ"] = "Refresh",
        ["azp"] = grant.Client.ClientId,
        ["sid"] = grant.Session.Id,
        ["cnf"] = Confirmation(grant.Jkt),
        ["scope"] = string.Join(' ', scopes),
    };

    private static void AddUserClaims(Dictionary<string, object?> claims, TwinUser user, IReadOnlyList<string> scopes)
    {
        if (scopes.Contains("email")) claims["email_verified"] = user.EmailVerified;
        if (scopes.Contains("profile"))
        {
            var name = string.Join(' ', new[] { user.FirstName, user.LastName }.Where(n => !string.IsNullOrEmpty(n)));
            claims["name"] = name.Length > 0 ? name : null;
            claims["preferred_username"] = user.Username;
            claims["given_name"] = user.FirstName;
            claims["family_name"] = user.LastName;
        }
        if (scopes.Contains("email")) claims["email"] = user.Email;
    }

    private static string VisibleScope(IReadOnlyList<string> scopes) =>
        string.Join(' ', VisibleScopes.Where(scopes.Contains));

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    private static string TokenType(string? jkt) => jkt is null ? "Bearer" : "DPoP";

    private static Dictionary<string, string>? Confirmation(string? jkt) =>
        jkt is null ? null : new Dictionary<string, string> { ["jkt"] = jkt, ["kc-jkt-type"] = "DPoP" };
}
