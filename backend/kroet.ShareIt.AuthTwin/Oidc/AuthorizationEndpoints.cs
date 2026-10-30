using System.Text.RegularExpressions;
using kroet.ShareIt.AuthTwin.Hosting;
using kroet.ShareIt.AuthTwin.Realm;
using kroet.ShareIt.AuthTwin.Store;
using Microsoft.AspNetCore.WebUtilities;

namespace kroet.ShareIt.AuthTwin.Oidc;

/// <summary>
/// Authorization code flow with Keycloak's checks and error answers. No SSO reuse: every authorization
/// shows the form, and <c>prompt=none</c> always answers <c>login_required</c>.
/// </summary>
internal static partial class AuthorizationEndpoints
{
    public static void MapAuthorization(this IEndpointRouteBuilder app)
    {
        var realm = app.MapGroup("/realms/{realm}").AddEndpointFilter(DiscoveryEndpoints.RealmExists);
        realm.MapGet("/protocol/openid-connect/auth", Authorize);
        realm.MapPost("/login-actions/authenticate", Authenticate);
    }

    private static IResult Authorize(HttpRequest request, RealmModel realm, AuthStore store, UserStore users, TwinUrls urls, TimeProvider time)
    {
        var query = request.Query;
        string? Param(string name) => query[name].FirstOrDefault() is { Length: > 0 } value ? value : null;

        var clientId = Param("client_id");
        if (clientId is null) return LoginPages.Error(realm, "Invalid request.");
        var client = realm.FindClient(clientId);
        if (client is null) return LoginPages.Error(realm, "Client not found.");

        var redirectUri = Param("redirect_uri");
        if (redirectUri is null || !ClientUris.Matches(client.RedirectUris, redirectUri))
            return LoginPages.Error(realm, "Invalid parameter: redirect_uri");

        var state = Param("state");
        IResult Fail(string error, string? description = null) =>
            Results.Redirect(ErrorRedirect(redirectUri, error, description, state, urls.Issuer));

        var responseType = Param("response_type");
        if (responseType is null) return Fail("invalid_request", "Missing parameter: response_type");
        if (responseType != "code")
            return Fail("unsupported_response_type", "Only the authorization code flow is supported.");
        if (!client.StandardFlowEnabled)
            return Fail("unauthorized_client", "Client is not allowed to initiate browser login with given response_type.");

        var scope = Param("scope") ?? "";
        if (!TokenFactory.AllowsScopes(client, scope)) return Fail("invalid_scope", $"Invalid scopes: {scope}");

        switch (Param("prompt"))
        {
            case "none":
                return Fail("login_required");
            case "create":
                return LoginPages.Error(realm, "Registration is not allowed.");
        }

        var challenge = Param("code_challenge");
        var method = Param("code_challenge_method");
        if (client.RequirePkce || challenge is not null || method is not null)
        {
            if (method is null) return Fail("invalid_request", "Missing parameter: code_challenge_method");
            if (challenge is null) return Fail("invalid_request", "Missing parameter: code_challenge");
            if (method != "S256") return Fail("invalid_request", "Invalid parameter: code challenge method is not matching the configured one");
            if (!PkceValue().IsMatch(challenge)) return Fail("invalid_request", "Invalid parameter: code_challenge");
        }

        var login = store.StartLogin(new AuthRequest(
            client.ClientId, redirectUri, state, Param("nonce"),
            scope.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            challenge, Param("dpop_jkt"),
            time.GetUtcNow() + AuthStore.LoginLifespan));
        return LoginPages.Form(realm, FormAction(urls, login), users.People);
    }

    private static async Task<IResult> Authenticate(HttpRequest request, RealmModel realm, AuthStore store, UserStore users, TwinUrls urls)
    {
        var loginId = request.Query["tx"].FirstOrDefault();
        var authRequest = loginId is null ? null : store.FindLogin(loginId);
        if (authRequest is null) return LoginPages.Error(realm, "This page is no longer valid.");

        var form = request.HasFormContentType ? await request.ReadFormAsync() : null;
        var user = users.FindByLogin(form?["username"].FirstOrDefault() ?? "");
        if (user is null || !user.Enabled || !user.HasPassword)
            return LoginPages.Form(realm, FormAction(urls, loginId!), users.People, LoginPages.InvalidCredentials);

        store.EndLogin(loginId!);
        var session = store.StartSession(user.Id);
        var code = store.IssueCode(authRequest, session);
        var redirect = QueryHelpers.AddQueryString(authRequest.RedirectUri, new Dictionary<string, string?>
        {
            ["state"] = authRequest.State,
            ["session_state"] = session.Id,
            ["iss"] = urls.Issuer,
            ["code"] = code,
        }.Where(p => p.Value is not null));
        return Results.Redirect(redirect);
    }

    private static string FormAction(TwinUrls urls, string loginId) => $"{urls.Issuer}/login-actions/authenticate?tx={loginId}";

    private static string ErrorRedirect(string redirectUri, string error, string? description, string? state, string issuer)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["error"] = error,
            ["error_description"] = description,
            ["state"] = state,
            ["iss"] = issuer,
        }.Where(p => p.Value is not null);
        return QueryHelpers.AddQueryString(redirectUri, parameters);
    }

    // RFC 7636: 43 to 128 unreserved characters.
    [GeneratedRegex("^[A-Za-z0-9._~-]{43,128}$")]
    private static partial Regex PkceValue();
}
