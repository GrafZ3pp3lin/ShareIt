using System.Text.Json;

namespace kroet.ShareIt.AuthTwin.Realm;

/// <summary>
/// Loads the realm file that describes the local client and users. Every key is either modeled
/// or explicitly ignored; a value the twin can't honor stops startup instead of making the twin
/// kinder than Keycloak.
/// </summary>
internal static class RealmFile
{
    private static readonly HashSet<string> KnownDefaultScopes =
        ["basic", "web-origins", "acr", "profile", "roles", "email"];

    private const string AudienceMapper = "oidc-audience-mapper";

    public static RealmModel Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return Read(document.RootElement);
    }

    public static RealmModel Read(JsonElement root)
    {
        var realm = new StrictJsonObject(root, "realm file");
        var name = realm.String("realm");

        var clients = realm.Objects("clients").Select(ReadClient).ToList();
        EnsureUnique(clients.Select(c => c.ClientId), "realm file.clients");

        var users = realm.Objects("users").Select(ReadUser).ToList();
        EnsureUnique(users.Select(u => u.Username), "realm file.users (username)");
        EnsureUnique(users.Where(u => u.Email is not null).Select(u => u.Email!), "realm file.users (email)");

        var model = new RealmModel(
            name,
            realm.OptionalString("defaultLocale") ?? "en",
            Seconds(realm, "accessTokenLifespan"),
            Seconds(realm, "ssoSessionIdleTimeout"),
            Seconds(realm, "ssoSessionMaxLifespan"),
            clients,
            users);

        realm.EnsureAllRead();
        return model;
    }

    private static ClientModel ReadClient(StrictJsonObject c)
    {
        var clientId = c.String("clientId");

        var publicClient = c.Bool("publicClient", false);
        if (!publicClient)
            throw StrictJsonObject.Error($"{c.Path}.publicClient", "must be true: the twin models public clients only");

        var standardFlow = c.Bool("standardFlowEnabled", true);
        if (!standardFlow)
            throw StrictJsonObject.Error($"{c.Path}.standardFlowEnabled", "must be true: the twin models the authorization code flow only");

        var redirectUris = c.Strings("redirectUris");
        var webOrigins = c.Strings("webOrigins");

        var requirePkce = false;
        var dpop = false;
        IReadOnlyList<string> postLogoutRedirectUris = [];
        if (c.OptionalObject("attributes") is { } attributes)
        {
            var pkce = attributes.OptionalString("pkce.code.challenge.method");
            if (pkce is not null and not "S256")
                throw StrictJsonObject.Error($"{attributes.Path}.pkce.code.challenge.method", "must be S256: the twin doesn't model anything else");
            requirePkce = pkce is not null;

            postLogoutRedirectUris = attributes.OptionalString("post.logout.redirect.uris")?
                .Split("##", StringSplitOptions.RemoveEmptyEntries) ?? [];

            dpop = attributes.OptionalString("dpop.bound.access.tokens") switch
            {
                null or "false" => false,
                "true" => true,
                _ => throw StrictJsonObject.Error($"{attributes.Path}.dpop.bound.access.tokens", "must be \"true\" or \"false\"")
            };
            attributes.EnsureAllRead();
        }

        var defaultScopes = c.Strings("defaultClientScopes");
        foreach (var scope in defaultScopes.Where(s => !KnownDefaultScopes.Contains(s)))
            throw StrictJsonObject.Error($"{c.Path}.defaultClientScopes", $"has a scope the twin doesn't model: {scope}");

        var optionalScopes = c.Strings("optionalClientScopes");
        if (optionalScopes.Count > 0)
            throw StrictJsonObject.Error($"{c.Path}.optionalClientScopes", "must be empty: the twin doesn't model optional scopes");

        string? audience = null;
        var mappers = c.Objects("protocolMappers");
        foreach (var mapper in mappers)
        {
            mapper.Ignore("name");
            mapper.Expect("protocol", "openid-connect");
            var type = mapper.String("protocolMapper");
            var config = mapper.OptionalObject("config") ?? throw StrictJsonObject.Error($"{mapper.Path}.config", "is required");
            switch (type)
            {
                case AudienceMapper when audience is null:
                    audience = config.String("included.custom.audience");
                    config.Expect("access.token.claim", "true");
                    config.Expect("id.token.claim", "false");
                    break;
                case AudienceMapper:
                    throw StrictJsonObject.Error(mapper.Path, $"is a second {AudienceMapper}: the twin models one per client");
                default:
                    throw StrictJsonObject.Error($"{mapper.Path}.protocolMapper", $"is a mapper the twin doesn't model: {type}");
            }

            config.EnsureAllRead();
            mapper.EnsureAllRead();
        }

        c.EnsureAllRead();
        return new ClientModel(
            clientId, publicClient, standardFlow, redirectUris, postLogoutRedirectUris, webOrigins,
            requirePkce, dpop, audience, defaultScopes, optionalScopes);
    }

    private static UserModel ReadUser(StrictJsonObject u)
    {
        var username = u.String("username").ToLowerInvariant();
        var email = u.OptionalString("email")?.ToLowerInvariant();
        // Only whether a password exists matters: the twin accepts any password, but Keycloak
        // refuses users who have none.
        var hasPassword = u.Objects("credentials").Count > 0;

        var user = new UserModel(
            username,
            email,
            u.Bool("emailVerified", false),
            u.Bool("enabled", false),
            u.OptionalString("firstName"),
            u.OptionalString("lastName"),
            hasPassword);

        u.EnsureAllRead();
        return user;
    }

    private static TimeSpan Seconds(StrictJsonObject o, string key) => TimeSpan.FromSeconds(o.Int(key));

    private static void EnsureUnique(IEnumerable<string> values, string path)
    {
        var duplicate = values.GroupBy(v => v, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) throw StrictJsonObject.Error(path, $"has a duplicate: {duplicate.Key}");
    }
}
