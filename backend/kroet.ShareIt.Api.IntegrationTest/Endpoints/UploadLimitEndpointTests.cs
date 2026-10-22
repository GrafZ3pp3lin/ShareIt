using System.Net;
using kroet.ShareIt.Api.Content.Dtos;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// Endpoint-level tests for the per-file upload size limits (413 enforcement).
///
/// Limits are tightened to keep bodies small: 1 MB for guests, 2 MB for
/// authenticated users, 2 MB for the whole request body. Kestrel's request
/// body limit is not exercised here because WebApplicationFactory serves
/// requests through TestServer; these tests pin the endpoint-level check that
/// rejects oversized files with a ProblemDetails 413 before anything is stored.
/// </summary>
[TestClass]
public sealed class UploadLimitEndpointTests : ShareItApiTestBase
{
    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["UploadSettings:AnonymousFileLimitMb"] = "1",
        ["UploadSettings:AuthenticatedFileLimitMb"] = "2",
        ["UploadSettings:MaxRequestBodySizeMb"] = "2",
    };

    // 1.5 MB: above the guest limit, below the request-body limit and the
    // authenticated limit, so it cleanly isolates the per-identity file check.
    private static readonly byte[] OversizedFile = new byte[1_500_000];

    [TestMethod]
    public async Task GetUploadLimits_ReturnsTightenedLimits()
    {
        // Guards against the overridden settings silently not being applied.
        var response = await Client.GetAsync("/api/v1/content/package/limits");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var limits = await ReadJsonAsync<UploadLimitsResponse>(response);
        Assert.AreEqual(1024L * 1024, limits.AnonymousFileLimitBytes);
        Assert.AreEqual(2 * 1024L * 1024, limits.AuthenticatedFileLimitBytes);
    }

    [TestMethod]
    public async Task CreatePackage_FileExceedingGuestLimit_Returns413()
    {
        var response = await SendCreatePackageAsync(fileBytes: OversizedFile, guestId: Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("File too large", problem.Title);
        Assert.IsTrue(problem.Detail!.Contains("hello.txt", StringComparison.Ordinal), problem.Detail);
        Assert.IsTrue(problem.Detail.Contains("1 MB", StringComparison.Ordinal), problem.Detail);
    }

    [TestMethod]
    public async Task CreatePackage_FileExactlyAtGuestLimit_IsAccepted()
    {
        // The endpoint rejects files strictly larger than the limit.
        var guestId = Guid.NewGuid();

        var response = await SendCreatePackageAsync(fileBytes: new byte[1024 * 1024], guestId: guestId);

        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, body);
        var package = await ReadJsonAsync<ContentPackageResponse>(response);
        Assert.AreEqual(guestId, package.UserId);
    }

    [TestMethod]
    public async Task CreatePackage_FileWithinGuestLimit_Returns201()
    {
        var response = await SendCreatePackageAsync(fileBytes: new byte[500_000], guestId: Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    [TestMethod]
    public async Task UploadFilesToPackage_FileExceedingGuestLimit_Returns413AndStoresNothing()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId, fileBytes: [1, 2, 3]);

        var response = await SendUploadFilesAsync(packageId, OversizedFile, guestId: guestId);

        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("File too large", problem.Title);

        // The oversized file must not have been persisted.
        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);
        Assert.HasCount(1, details.Items);
    }

    [TestMethod]
    public async Task UploadFilesToPackage_FileAboveGuestLimitWithinAuthenticatedLimit_SucceedsForAuthenticatedOwner()
    {
        var userId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(userId: userId, title: "limit test");

        var response = await SendUploadFilesAsync(packageId, OversizedFile, userId: userId);
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
        var package = await ReadJsonAsync<ContentPackageResponse>(response);
        Assert.AreEqual(1, package.ItemCount);
    }
}
