using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;

namespace kroet.ShareIt.Api.Authentication;

/// <summary>
/// Fallback authentication scheme, used only to answer authorization challenges.
/// The API authenticates via bearer tokens rather than the ASP.NET
/// authentication pipeline, so there is no interactive sign-in flow to redirect
/// to. Denied anonymous requests are answered with a plain 401 (challenge) or
/// 403 (forbid) instead of throwing "no DefaultChallengeScheme".
/// </summary>
public sealed class ApiChallengeHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiChallenge";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
        Task.FromResult(AuthenticateResult.NoResult());

    protected override Task HandleChallengeAsync(AuthenticationProperties? properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties? properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
