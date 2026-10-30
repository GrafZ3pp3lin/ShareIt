using kroet.ShareIt.AuthTwin.Realm;
using Microsoft.Extensions.Primitives;

namespace kroet.ShareIt.AuthTwin.Oidc;

/// <summary>What the endpoints share: client authentication, origin check, error answers.</summary>
internal static class ClientRequests
{
    private const string InvalidClient = "Invalid client or Invalid client credentials";

    /// <summary>Only public clients are modeled, and they name themselves in the form.</summary>
    public static ClientModel? Authenticate(IFormCollection form, RealmModel realm, out IResult? error)
    {
        var client = realm.FindClient(Value(form["client_id"]) ?? "");
        if (client is null || !client.PublicClient)
        {
            error = Error("invalid_client", InvalidClient, StatusCodes.Status401Unauthorized);
            return null;
        }

        error = null;
        return client;
    }

    /// <summary>A browser request from outside the client's web origins is refused; an allowed one gets CORS headers.</summary>
    public static IResult? CheckOrigin(HttpContext context, ClientModel client)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (origin.Length == 0) return null;
        if (!ClientUris.AllowsOrigin(client, origin))
            return Results.Json(new { error = "Invalid origin" }, statusCode: StatusCodes.Status403Forbidden);

        Cors.AllowOrigin(context);
        context.Response.Headers.AccessControlExposeHeaders = "Access-Control-Allow-Methods";
        return null;
    }

    public static async Task<IFormCollection> ReadFormAsync(HttpRequest request) =>
        request.HasFormContentType ? await request.ReadFormAsync() : FormCollection.Empty;

    public static string? Value(StringValues values) => values.FirstOrDefault() is { Length: > 0 } value ? value : null;

    public static IResult Error(string error, string description, int status = StatusCodes.Status400BadRequest) =>
        Results.Json(new Dictionary<string, string> { ["error"] = error, ["error_description"] = description }, statusCode: status);

    public static IResult MethodNotAllowed() =>
        Results.Json(new { error = "HTTP 405 Method Not Allowed" }, statusCode: StatusCodes.Status405MethodNotAllowed);
}
