using System.Net;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// Package expiry and empty-package endpoint behaviour against the real
/// Postgres and RustFS stores.
/// </summary>
[TestClass]
public sealed class PackageLifecycleEndpointTests : ShareItApiTestBase
{
    [TestMethod]
    public async Task GetPackage_WhenExpired_Returns410()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId, fileBytes: [1, 2, 3], timeToLiveHours: 1);

        TimeProvider.UtcNow = TimeProvider.UtcNow.AddHours(2);

        var response = await GetPackageAsync(packageId, guestId);

        Assert.AreEqual(HttpStatusCode.Gone, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Package expired", problem.Title);
    }

    [TestMethod]
    public async Task GetPackage_WhenPackageHasNoContent_Returns410()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);

        var response = await GetPackageAsync(packageId, guestId);

        Assert.AreEqual(HttpStatusCode.Gone, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Package is empty", problem.Title);
    }
}
