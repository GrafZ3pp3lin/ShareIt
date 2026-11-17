using System.Net;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// GET /package/{id}/content/{contentId}/download is limited per remote IP,
/// independently of the package-read limiter.
/// </summary>
[TestClass]
public sealed class DownloadRateLimitTests : ShareItApiTestBase
{
    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["RateLimitSettings:DownloadPermitLimit"] = "2",
        ["RateLimitSettings:DownloadWindowMinutes"] = "1"
    };

    [TestMethod]
    public async Task Download_AfterThePermitLimit_Returns429WithRetryAfter()
    {
        var guestId = Guid.NewGuid();
        var (packageId, contentId) = await CreatePackageWithItemAsync(
            bytes: LargeItemBytes, guestId: guestId, maxAccessCount: 1);
        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);
        var downloadUrl = DownloadUrl(packageId, contentId);

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var response = await SendDownloadAsync(
                downloadUrl,
                details.AccessSession.Token,
                guestId);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"Attempt {attempt} should be allowed.");
        }

        var limited = await SendDownloadAsync(
            downloadUrl,
            details.AccessSession.Token,
            guestId);

        Assert.AreEqual(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.IsNotNull(limited.Headers.RetryAfter);
        var problem = await ReadProblemAsync(limited);
        Assert.AreEqual("Too many requests", problem.Title);
    }
}
