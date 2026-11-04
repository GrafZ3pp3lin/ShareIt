using System.Net;
using kroet.ShareIt.Api.Content.Dtos;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// Endpoint-level tests for the per-file upload size limits (413 enforcement)
/// on the single-item upload endpoint.
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
        ["UploadSettings:Anonymous:FileLimitMb"] = "1",
        ["UploadSettings:Authenticated:FileLimitMb"] = "2",
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
    public async Task UploadItem_FileExceedingGuestLimit_Returns413()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);

        var response = await SendUploadItemAsync(packageId, "file", OversizedFile, guestId: guestId);

        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("File too large", problem.Title);
        Assert.IsTrue(problem.Detail!.Contains("1 MB", StringComparison.Ordinal), problem.Detail);
    }

    [TestMethod]
    public async Task UploadItem_FileExactlyAtGuestLimit_IsAccepted()
    {
        // The endpoint rejects files strictly larger than the limit.
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);

        var response = await SendUploadItemAsync(packageId, "file", new byte[1024 * 1024], guestId: guestId);

        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
        var package = await ReadJsonAsync<ContentPackageResponse>(response);
        Assert.AreEqual(1, package.ItemCount);
    }

    [TestMethod]
    public async Task UploadItem_FileWithinGuestLimit_Returns200()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);

        var response = await SendUploadItemAsync(packageId, "file", new byte[500_000], guestId: guestId);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task UploadItem_FileExceedingGuestLimit_StoresNothing()
    {
        var guestId = Guid.NewGuid();
        var (packageId, _) = await CreatePackageWithItemAsync(guestId: guestId);

        var response = await SendUploadItemAsync(packageId, "file", OversizedFile, guestId: guestId);

        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("File too large", problem.Title);

        // The oversized file must not have been persisted.
        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);
        Assert.HasCount(1, details.Items);
    }

    [TestMethod]
    public async Task UploadItem_FileAboveGuestLimitWithinAuthenticatedLimit_SucceedsForOwner()
    {
        var userId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(userId: userId, maxDownloadCount: 10, timeToLiveHours: 24);

        var response = await SendUploadItemAsync(packageId, "file", OversizedFile, userId: userId);
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
        var package = await ReadJsonAsync<ContentPackageResponse>(response);
        Assert.AreEqual(1, package.ItemCount);
    }
}
