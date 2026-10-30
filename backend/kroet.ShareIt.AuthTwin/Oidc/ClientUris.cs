using kroet.ShareIt.AuthTwin.Realm;

namespace kroet.ShareIt.AuthTwin.Oidc;

/// <summary>Keycloak's matching of redirect URIs and web origins against a client's settings.</summary>
internal static class ClientUris
{
    /// <summary>
    /// A pattern ending in <c>*</c> matches by prefix, case-sensitively; <c>https://host/*</c> also matches
    /// <c>https://host</c>. Anything else must match exactly.
    /// </summary>
    public static bool Matches(IEnumerable<string> patterns, string uri) => patterns.Any(pattern =>
    {
        if (!pattern.EndsWith('*')) return uri == pattern;
        var prefix = pattern[..^1];
        return uri.StartsWith(prefix, StringComparison.Ordinal) || (prefix.EndsWith('/') && uri == prefix[..^1]);
    });

    /// <summary>The origins a client may call from: <c>+</c> stands for the origins of its redirect URIs.</summary>
    public static IReadOnlyList<string> WebOrigins(ClientModel client) => client.WebOrigins
        .SelectMany(origin => origin == "+" ? client.RedirectUris.Select(OriginOf).OfType<string>() : [origin])
        .Distinct()
        .ToList();

    public static bool AllowsOrigin(ClientModel client, string origin)
    {
        var origins = WebOrigins(client);
        return origins.Contains("*") || origins.Contains(origin);
    }

    private static string? OriginOf(string redirectUri) =>
        Uri.TryCreate(redirectUri.TrimEnd('*'), UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : null;
}
