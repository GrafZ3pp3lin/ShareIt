namespace kroet.ShareIt.AuthTwin.Oidc;

/// <summary>Keycloak's CORS answers: the origin is reflected with credentials, never <c>*</c>.</summary>
internal static class Cors
{
    private const string AllowedHeaders =
        "Origin, Access-Control-Request-Method, X-Requested-With, Accept, Access-Control-Request-Headers, DPoP, Content-Type, Authorization";

    public static void AllowOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (origin.Length == 0) return;
        context.Response.Headers.AccessControlAllowOrigin = origin;
        context.Response.Headers.AccessControlAllowCredentials = "true";
    }

    /// <summary>Keycloak answers every preflight; the real request is where a foreign origin gets refused.</summary>
    public static IResult Preflight(HttpContext context, string methods)
    {
        AllowOrigin(context);
        context.Response.Headers.AccessControlAllowMethods = methods;
        context.Response.Headers.AccessControlAllowHeaders = AllowedHeaders;
        context.Response.Headers.AccessControlMaxAge = "3600";
        return Results.Ok();
    }
}
