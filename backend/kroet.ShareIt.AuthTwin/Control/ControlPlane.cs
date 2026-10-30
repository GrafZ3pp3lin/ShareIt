using System.Text.Json;
using System.Text.Json.Serialization;
using kroet.ShareIt.AuthTwin.Hosting;
using kroet.ShareIt.AuthTwin.Oidc;
using kroet.ShareIt.AuthTwin.Realm;
using kroet.ShareIt.AuthTwin.Store;

namespace kroet.ShareIt.AuthTwin.Control;

/// <summary>
/// Tests and local development set the twin up here: list users, issue tokens without a browser,
/// and end sessions. No authentication, so only loopback callers are served.
/// </summary>
internal static class ControlPlane
{
    private const string DefaultScope = "openid profile";

    private static readonly JsonSerializerOptions Strict = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static void MapControlPlane(this IEndpointRouteBuilder app)
    {
        var twin = app.MapTwinControlPlane();
        twin.MapGet("/users", (UserStore users) => Results.Json(users.People.Select(View)));
        twin.MapGet("/users/{id}", (string id, UserStore users) => users.Find(id) is { } user ? Results.Json(View(user)) : NotFound());
        twin.MapPost("/users/{id}/tokens", IssueTokens);
        twin.MapPost("/users/{id}/revoke-sessions", RevokeSessions);
    }

    /// <param name="IssuedAt">Backdates the tokens, e.g. to get an expired access token.</param>
    internal sealed record TokenSpec(
        string ClientId,
        string? Scope = DefaultScope,
        string? Jkt = null,
        DateTimeOffset? IssuedAt = null,
        int? ExpiresIn = null);

    /// <summary>
    /// Signs the user in without the browser: a real session and the same tokens the token endpoint would
    /// give, so refresh and logout work on them. Users Keycloak wouldn't let sign in get no tokens.
    /// </summary>
    private static async Task<IResult> IssueTokens(
        string id, HttpRequest request, UserStore users, AuthStore store, RealmModel realm, TokenFactory tokens)
    {
        var spec = await ReadAsync<TokenSpec>(request);
        if (spec is null) return BadRequest("body must be a token request: clientId, scope, jkt, issuedAt, expiresIn");
        var user = users.Find(id);
        if (user is null) return NotFound();
        var client = realm.FindClient(spec.ClientId ?? "");
        if (client is null || !client.StandardFlowEnabled) return BadRequest($"no client for user sign-in: {spec.ClientId}");
        var scope = spec.Scope ?? DefaultScope;
        if (!TokenFactory.AllowsScopes(client, scope)) return BadRequest($"scopes the client doesn't have: {scope}");
        if (client.DpopBoundAccessTokens && spec.Jkt is null) return BadRequest($"{client.ClientId} requires DPoP bound tokens: pass a jkt");
        if (!user.Enabled || !user.HasPassword) return BadRequest("Keycloak wouldn't let this user sign in: disabled or no password");

        var session = store.StartSession(user.Id);
        var scopes = scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var expiresIn = spec.ExpiresIn is { } seconds ? TimeSpan.FromSeconds(seconds) : (TimeSpan?)null;
        return Results.Json(tokens.UserTokens(new UserGrant(user, client, scopes, session, null, spec.Jkt), spec.IssuedAt, expiresIn));
    }

    private static IResult RevokeSessions(string id, UserStore users, AuthStore store)
    {
        if (users.Find(id) is null) return NotFound();
        return Results.Json(new { ended = store.EndUserSessions(id) });
    }

    private static object View(TwinUser user) => new
    {
        user.Id,
        user.Email,
        user.FirstName,
        user.LastName,
        user.Enabled,
        user.EmailVerified,
        HasPassword = user.HasPassword,
    };

    private static async Task<T?> ReadAsync<T>(HttpRequest request) where T : class
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(request.Body, Strict);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IResult BadRequest(string error) =>
        Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest);

    private static IResult NotFound() =>
        Results.Json(new { error = "no such user" }, statusCode: StatusCodes.Status404NotFound);
}
