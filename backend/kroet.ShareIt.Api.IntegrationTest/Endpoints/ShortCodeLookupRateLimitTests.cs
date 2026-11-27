using System.Net;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// Short-code lookups are limited per remote IP, independently of package
/// reads. The lookup itself never claims an access.
/// </summary>
[TestClass]
public sealed class ShortCodeLookupRateLimitTests : ShareItApiTestBase
{
    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["RateLimitSettings:ShortCodeLookupPermitLimit"] = "2",
        ["RateLimitSettings:ShortCodeLookupWindowMinutes"] = "15"
    };

    [TestMethod]
    public async Task ResolveShortCode_AfterThePermitLimit_Returns429WithRetryAfter()
    {
        var coded = await CreateShortCodePackageAsync();

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var response = await ResolveShortCodeAsync(coded.ShortCode!);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"Attempt {attempt} should be allowed.");
        }

        var limited = await ResolveShortCodeAsync(coded.ShortCode!, origin: "http://localhost:3001");

        Assert.AreEqual(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.IsNotNull(limited.Headers.RetryAfter);
        Assert.AreEqual("no-store", limited.Headers.CacheControl?.ToString());
        Assert.IsTrue(limited.Headers.TryGetValues("Access-Control-Expose-Headers", out var exposed));
        Assert.IsTrue(exposed.Contains("Retry-After", StringComparer.OrdinalIgnoreCase));
        var problem = await ReadProblemAsync(limited);
        Assert.AreEqual("Too many requests", problem.Title);
    }
}
