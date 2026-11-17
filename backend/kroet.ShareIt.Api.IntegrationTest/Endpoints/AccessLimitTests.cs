using System.Net;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// Endpoint-level tests for the per-identity package access limits.
///
/// The spec values apply: guests may allow at most 1 access per package (and
/// must set a limit at all), authenticated users at most 10 — or omit the
/// limit entirely for unlimited access.
/// </summary>
[TestClass]
public sealed class AccessLimitTests : ShareItApiTestBase
{
    [TestMethod]
    public async Task CreatePackage_GuestAboveAccessLimit_Returns400()
    {
        var response = await SendCreatePackageAsync(guestId: Guid.NewGuid(), maxAccessCount: 2);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Access limit too high", problem.Title);
        Assert.IsTrue(problem.Detail!.Contains("at most 1", StringComparison.Ordinal), problem.Detail);
    }

    [TestMethod]
    public async Task CreatePackage_GuestAtAccessLimit_IsAccepted()
    {
        var response = await SendCreatePackageAsync(guestId: Guid.NewGuid(), maxAccessCount: 1);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    [TestMethod]
    public async Task CreatePackage_GuestWithoutAccessLimit_Returns400()
    {
        var response = await SendCreatePackageAsync(guestId: Guid.NewGuid(), maxAccessCount: null);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Access limit required", problem.Title);
    }

    [TestMethod]
    public async Task CreatePackage_NonPositiveAccessLimit_Returns400()
    {
        var response = await SendCreatePackageAsync(guestId: Guid.NewGuid(), maxAccessCount: 0);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Invalid access limit", problem.Title);
    }

    [TestMethod]
    public async Task CreatePackage_AuthenticatedAboveAccessLimit_Returns400()
    {
        var response = await SendCreatePackageAsync(userId: Guid.NewGuid(), maxAccessCount: 11);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Access limit too high", problem.Title);
        Assert.IsTrue(problem.Detail!.Contains("at most 10", StringComparison.Ordinal), problem.Detail);
    }

    [TestMethod]
    public async Task CreatePackage_AuthenticatedAtAccessLimit_IsAccepted()
    {
        var response = await SendCreatePackageAsync(userId: Guid.NewGuid(), maxAccessCount: 10);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    [TestMethod]
    public async Task CreatePackage_AuthenticatedWithoutAccessLimit_IsAccepted()
    {
        // Omitting the access limit (unlimited access) remains allowed for authenticated users.
        var response = await SendCreatePackageAsync(userId: Guid.NewGuid(), maxAccessCount: null);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }
}
