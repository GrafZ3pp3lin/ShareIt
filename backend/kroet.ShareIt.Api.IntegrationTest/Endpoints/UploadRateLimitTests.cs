using System.Net;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// POST /package and PUT /package/{id}/content/{contentId} share the
/// per-remote-IP upload rate-limit policy, so one caller cannot flood
/// packages with empty creations or item uploads.
/// </summary>
[TestClass]
public sealed class UploadRateLimitTests : ShareItApiTestBase
{
    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["RateLimitSettings:UploadPermitLimit"] = "2",
        ["RateLimitSettings:UploadWindowMinutes"] = "1"
    };

    [TestMethod]
    public async Task CreatePackage_AfterThePermitLimit_Returns429WithRetryAfter()
    {
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var response = await SendCreatePackageAsync();
            var body = await response.Content.ReadAsStringAsync();
            Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, $"Attempt {attempt} should be allowed. {body}");
        }

        var limited = await SendCreatePackageAsync();

        Assert.AreEqual(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.IsNotNull(limited.Headers.RetryAfter);
        Assert.AreEqual("no-store", limited.Headers.CacheControl?.ToString());
        var problem = await ReadProblemAsync(limited);
        Assert.AreEqual("Too many requests", problem.Title);
    }

    [TestMethod]
    public async Task Upload_AfterThePermitLimit_Returns429WithRetryAfter()
    {
        // Package creation shares the upload budget and consumes one permit,
        // leaving exactly one permit for item uploads.
        var packageId = await CreatePackageAsync();

        var allowed = await SendUploadItemAsync(packageId, "file", SmallItemBytes);
        var allowedBody = await allowed.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, allowed.StatusCode, allowedBody);

        var limited = await SendUploadItemAsync(packageId, "file", SmallItemBytes);

        Assert.AreEqual(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.IsNotNull(limited.Headers.RetryAfter);
        Assert.AreEqual("no-store", limited.Headers.CacheControl?.ToString());
        var problem = await ReadProblemAsync(limited);
        Assert.AreEqual("Too many requests", problem.Title);
    }
}
