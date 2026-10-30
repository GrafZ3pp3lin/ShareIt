namespace kroet.ShareIt.AuthTwin.Realm;

internal sealed record RealmModel(
    string Name,
    string DefaultLocale,
    TimeSpan AccessTokenLifespan,
    TimeSpan SsoSessionIdleTimeout,
    TimeSpan SsoSessionMaxLifespan,
    IReadOnlyList<ClientModel> Clients,
    IReadOnlyList<UserModel> Users)
{
    public ClientModel? FindClient(string clientId) => Clients.FirstOrDefault(c => c.ClientId == clientId);
}

internal sealed record ClientModel(
    string ClientId,
    bool PublicClient,
    bool StandardFlowEnabled,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> PostLogoutRedirectUris,
    IReadOnlyList<string> WebOrigins,
    bool RequirePkce,
    bool DpopBoundAccessTokens,
    string? Audience,
    IReadOnlyList<string> DefaultScopes,
    IReadOnlyList<string> OptionalScopes);

internal sealed record UserModel(
    string Username,
    string? Email,
    bool EmailVerified,
    bool Enabled,
    string? FirstName,
    string? LastName,
    bool HasPassword);
