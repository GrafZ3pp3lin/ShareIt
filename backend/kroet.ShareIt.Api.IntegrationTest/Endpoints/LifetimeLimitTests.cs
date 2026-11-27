using System.Net;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// Endpoint-level tests for the per-identity package lifetime limits.
///
/// Limits are tightened so both identities can be exercised quickly:
/// guests may live for at most 6 hours, authenticated users for at most 24.
/// Every package must set a lifetime: there is no "no expiration" option.
/// </summary>
[TestClass]
public sealed class LifetimeLimitTests : ShareItApiTestBase
{
    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["UploadSettings:Anonymous:MaxLifetimeHours"] = "6",
        ["UploadSettings:Authenticated:MaxLifetimeHours"] = "24",
    };

    [TestMethod]
    public async Task GetUploadLimits_ReturnsConfiguredLifetimeMaxima()
    {
        var response = await Client.GetAsync("/api/v1/content/package/limits");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var limits = await ReadJsonAsync<kroet.ShareIt.Api.Content.Dtos.UploadLimitsResponse>(response);
        Assert.AreEqual(6, limits.AnonymousMaxLifetimeHours);
        Assert.AreEqual(24, limits.AuthenticatedMaxLifetimeHours);
        Assert.AreEqual(1, limits.AnonymousMaxAccessCount);
        Assert.AreEqual(10, limits.AuthenticatedMaxAccessCount);
    }

    [TestMethod]
    public async Task CreatePackage_GuestAboveLifetimeLimit_Returns400()
    {
        var response = await SendCreatePackageAsync(timeToLiveHours: 7);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Lifetime too long", problem.Title);
        Assert.IsTrue(problem.Detail!.Contains("6 hours", StringComparison.Ordinal), problem.Detail);
    }

    [TestMethod]
    public async Task CreatePackage_GuestAtLifetimeLimit_IsAccepted()
    {
        var response = await SendCreatePackageAsync(timeToLiveHours: 6);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    [TestMethod]
    public async Task CreatePackage_GuestWithoutLifetime_Returns400()
    {
        var response = await SendCreatePackageAsync(timeToLiveHours: null);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Invalid lifetime", problem.Title);
    }

    [TestMethod]
    public async Task CreatePackage_NonPositiveLifetime_Returns400()
    {
        var response = await SendCreatePackageAsync(timeToLiveHours: 0);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Invalid lifetime", problem.Title);
    }

    [TestMethod]
    public async Task CreatePackage_AuthenticatedAboveLifetimeLimit_Returns400()
    {
        var response = await SendCreatePackageAsync(userId: Guid.NewGuid(), timeToLiveHours: 25);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Lifetime too long", problem.Title);
        Assert.IsTrue(problem.Detail!.Contains("24 hours", StringComparison.Ordinal), problem.Detail);
    }

    [TestMethod]
    public async Task CreatePackage_AuthenticatedAtLifetimeLimit_IsAccepted()
    {
        var response = await SendCreatePackageAsync(userId: Guid.NewGuid(), timeToLiveHours: 24);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    [TestMethod]
    public async Task CreatePackage_AuthenticatedWithoutLifetime_Returns400()
    {
        // Every package must set a lifetime so abandoned uploads always expire.
        var response = await SendCreatePackageAsync(userId: Guid.NewGuid(), timeToLiveHours: null);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Invalid lifetime", problem.Title);
    }
}
