using System.Net;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// GET /package/{id} is limited per (package id, remote IP). Successful
/// unlocks and failed attempts share the same budget: a link is meant to be
/// opened once, and guessing a passphrase must not be free.
/// </summary>
[TestClass]
public sealed class PackageReadRateLimitTests : ShareItApiTestBase
{
    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["RateLimitSettings:PackageReadPermitLimit"] = "2",
        ["RateLimitSettings:PackageReadWindowMinutes"] = "15"
    };

    [TestMethod]
    public async Task GetPackage_AfterThePermitLimit_Returns429WithRetryAfter()
    {
        var ownerId = Guid.NewGuid();
        var (packageId, _) = await CreatePackageWithItemAsync(
            userId: ownerId, maxAccessCount: null, timeToLiveHours: 24);
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var response = await GetPackageAsync(packageId);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"Attempt {attempt} should be allowed.");
        }

        var limited = await GetPackageAsync(packageId);

        Assert.AreEqual(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.IsNotNull(limited.Headers.RetryAfter);
        Assert.AreEqual("no-store", limited.Headers.CacheControl?.ToString());
        var problem = await ReadProblemAsync(limited);
        Assert.AreEqual("Too many requests", problem.Title);
    }

    [TestMethod]
    public async Task GetPackage_FailedUnlocksAlsoCountTowardsTheLimit()
    {
        var (packageId, _) = await CreatePackageWithItemAsync(
            maxAccessCount: 1, unlockVerifier: Verifier());

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var response = await GetPackageAsync(packageId, unlockProof: Verifier(9));
            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var limited = await GetPackageAsync(packageId, unlockProof: Verifier(9));

        Assert.AreEqual(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.IsNotNull(limited.Headers.RetryAfter);
    }
}
