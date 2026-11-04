using System.Net;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// Endpoint-level tests for the per-identity package access (download) limits.
///
/// The spec values apply: guests may allow at most 1 access per package (and
/// must set a limit at all), authenticated users at most 10 — or omit the
/// limit entirely for unlimited access.
/// </summary>
[TestClass]
public sealed class DownloadLimitTests : ShareItApiTestBase
{
    [TestMethod]
    public async Task CreatePackage_GuestAboveDownloadLimit_Returns400()
    {
        var response = await SendCreatePackageAsync(guestId: Guid.NewGuid(), maxDownloadCount: 2);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Download limit too high", problem.Title);
        Assert.IsTrue(problem.Detail!.Contains("at most 1", StringComparison.Ordinal), problem.Detail);
    }

    [TestMethod]
    public async Task CreatePackage_GuestAtDownloadLimit_IsAccepted()
    {
        var response = await SendCreatePackageAsync(guestId: Guid.NewGuid(), maxDownloadCount: 1);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    [TestMethod]
    public async Task CreatePackage_GuestWithoutDownloadLimit_Returns400()
    {
        var response = await SendCreatePackageAsync(guestId: Guid.NewGuid(), maxDownloadCount: null);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Download limit required", problem.Title);
    }

    [TestMethod]
    public async Task CreatePackage_NonPositiveDownloadLimit_Returns400()
    {
        var response = await SendCreatePackageAsync(guestId: Guid.NewGuid(), maxDownloadCount: 0);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Invalid download limit", problem.Title);
    }

    [TestMethod]
    public async Task CreatePackage_AuthenticatedAboveDownloadLimit_Returns400()
    {
        var response = await SendCreatePackageAsync(userId: Guid.NewGuid(), maxDownloadCount: 11);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Download limit too high", problem.Title);
        Assert.IsTrue(problem.Detail!.Contains("at most 10", StringComparison.Ordinal), problem.Detail);
    }

    [TestMethod]
    public async Task CreatePackage_AuthenticatedAtDownloadLimit_IsAccepted()
    {
        var response = await SendCreatePackageAsync(userId: Guid.NewGuid(), maxDownloadCount: 10);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    [TestMethod]
    public async Task CreatePackage_AuthenticatedWithoutDownloadLimit_IsAccepted()
    {
        // Omitting the access limit (unlimited access) remains allowed for authenticated users.
        var response = await SendCreatePackageAsync(userId: Guid.NewGuid(), maxDownloadCount: null);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }
}
