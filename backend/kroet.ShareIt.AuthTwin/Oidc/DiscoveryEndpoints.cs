using kroet.ShareIt.AuthTwin.Hosting;
using kroet.ShareIt.AuthTwin.Realm;

namespace kroet.ShareIt.AuthTwin.Oidc;

internal static class DiscoveryEndpoints
{
    public static void MapDiscovery(this IEndpointRouteBuilder app)
    {
        var realm = app.MapGroup("/realms/{realm}").AddEndpointFilter(RealmExists);

        realm.MapGet("/.well-known/openid-configuration", (HttpContext context, TwinUrls urls) =>
        {
            Cors.AllowOrigin(context);
            context.Response.Headers.CacheControl = "no-cache, must-revalidate, no-transform, no-store";
            return Results.Json(Discovery(urls));
        });
        realm.MapGet("/protocol/openid-connect/certs", (HttpContext context, SigningKey key) =>
        {
            Cors.AllowOrigin(context);
            context.Response.Headers.CacheControl = "no-cache";
            return Results.Json(new { keys = new[] { key.ToJwk() } });
        });
        realm.MapMethods("/.well-known/openid-configuration", ["OPTIONS"], (HttpContext context) => Cors.Preflight(context, "GET"));
        realm.MapMethods("/protocol/openid-connect/certs", ["OPTIONS"], (HttpContext context) => Cors.Preflight(context, "GET"));
    }

    /// <summary>Realm names match case-sensitively, as in Keycloak.</summary>
    public static async ValueTask<object?> RealmExists(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var name = invocation.HttpContext.GetRouteValue("realm") as string;
        var realm = invocation.HttpContext.RequestServices.GetRequiredService<RealmModel>();
        if (name != realm.Name) return Results.Json(new { error = "Realm does not exist" }, statusCode: StatusCodes.Status404NotFound);
        return await next(invocation);
    }

    private static Dictionary<string, object> Discovery(TwinUrls urls) => new()
    {
        ["issuer"] = urls.Issuer,
        ["authorization_endpoint"] = urls.OidcEndpoint("auth"),
        ["token_endpoint"] = urls.OidcEndpoint("token"),
        ["end_session_endpoint"] = urls.OidcEndpoint("logout"),
        ["jwks_uri"] = urls.OidcEndpoint("certs"),
        ["grant_types_supported"] = new[] { "authorization_code", "refresh_token" },
        ["response_types_supported"] = new[] { "code" },
        ["response_modes_supported"] = new[] { "query" },
        ["subject_types_supported"] = new[] { "public" },
        ["id_token_signing_alg_values_supported"] = new[] { "RS256" },
        ["code_challenge_methods_supported"] = new[] { "S256" },
        ["dpop_signing_alg_values_supported"] = new[] { "ES256" },
        ["token_endpoint_auth_methods_supported"] = new[] { "none" },
        ["scopes_supported"] = new[] { "openid", "basic", "web-origins", "acr", "profile", "roles", "email" },
        ["authorization_response_iss_parameter_supported"] = true,
    };
}
