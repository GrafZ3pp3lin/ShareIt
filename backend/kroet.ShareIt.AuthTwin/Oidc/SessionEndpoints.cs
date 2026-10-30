using System.Text.Json;
using kroet.ShareIt.AuthTwin.Hosting;
using kroet.ShareIt.AuthTwin.Realm;
using kroet.ShareIt.AuthTwin.Store;
using Microsoft.AspNetCore.WebUtilities;
using static kroet.ShareIt.AuthTwin.Oidc.ClientRequests;

namespace kroet.ShareIt.AuthTwin.Oidc;

/// <summary>Logout with Keycloak's answers. Logout ends only the SSO session.</summary>
internal static class SessionEndpoints
{
    public static void MapSessions(this IEndpointRouteBuilder app)
    {
        var oidc = app.MapGroup("/realms/{realm}/protocol/openid-connect").AddEndpointFilter(DiscoveryEndpoints.RealmExists);
        oidc.MapMethods("/logout", ["GET", "POST"], Logout);
    }

    private static async Task<IResult> Logout(HttpRequest request, RealmModel realm, AuthStore store, TwinUrls urls, SigningKey key)
    {
        var parameters = request.HasFormContentType
            ? (await request.ReadFormAsync()).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)
            : request.Query.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        string? Param(string name) => parameters.TryGetValue(name, out var value) ? Value(value) : null;

        var hint = Param("id_token_hint");
        var clientId = Param("client_id");
        JsonElement? idToken = null;
        if (hint is not null)
        {
            idToken = Jwt.Read(hint, key);
            if (idToken is not { } claims || claims.String("typ") != "ID" || claims.String("iss") != urls.Issuer
                || (clientId is not null && claims.String("azp") != clientId))
                return LoginPages.Error(realm, "Invalid parameter: id_token_hint");
            clientId = claims.String("azp");
        }

        var redirectUri = Param("post_logout_redirect_uri");
        if (redirectUri is not null)
        {
            if (clientId is null) return LoginPages.Error(realm, "Missing parameter: id_token_hint");
            var client = realm.FindClient(clientId);
            if (client is null || !ClientUris.Matches(client.PostLogoutRedirectUris, redirectUri))
                return LoginPages.Error(realm, "Invalid redirect URI");
        }

        if (idToken?.String("sid") is { } sessionId) store.EndSession(sessionId);

        if (redirectUri is not null)
            return Results.Redirect(Param("state") is { } state ? QueryHelpers.AddQueryString(redirectUri, "state", state) : redirectUri);
        // Without a hint Keycloak asks for confirmation through its session cookie, which the twin doesn't keep.
        return idToken is null
            ? LoginPages.Message(realm, "Do you want to sign out?", "Sign out")
            : LoginPages.Message(realm, "You are signed out.");
    }
}
