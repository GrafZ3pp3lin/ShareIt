using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace kroet.ShareIt.Api.IntegrationTest.Infrastructure;

/// <summary>
/// Test-only authentication scheme for WebApplicationFactory runs.
/// Authenticates the request as a fixed user when the <c>X-Test-User</c> header
/// carries a GUID; otherwise returns NoResult so the request stays anonymous
/// and guest flows (via the X-Guest-ID header) behave exactly like production.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestAuth";

    public const string UserIdHeader = "X-Test-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Context.Request.Headers.TryGetValue(UserIdHeader, out var values)
            || !Guid.TryParse(values.ToString(), out var userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity([new Claim("sub", userId.ToString())], SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
