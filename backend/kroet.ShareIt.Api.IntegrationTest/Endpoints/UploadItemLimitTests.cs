using System.Net;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// Bounding package size: the per-package item cap rejects further uploads
/// after the configured maximum, without breaking idempotent retries.
/// </summary>
[TestClass]
public sealed class UploadItemLimitTests : ShareItApiTestBase
{
    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["UploadSettings:MaxItemsPerPackage"] = "2"
    };

    [TestMethod]
    public async Task UploadItem_AtMaxItems_IsAccepted()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);

        var first = await SendUploadItemAsync(packageId, "file", SmallItemBytes, guestId: guestId);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        var second = await SendUploadItemAsync(packageId, "file", SmallItemBytes, guestId: guestId);
        var secondBody = await second.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode, secondBody);
    }

    [TestMethod]
    public async Task UploadItem_AboveMaxItems_Returns400AndStoresNothing()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);

        await SendUploadItemAsync(packageId, "file", SmallItemBytes, guestId: guestId);
        await SendUploadItemAsync(packageId, "file", SmallItemBytes, guestId: guestId);

        var response = await SendUploadItemAsync(packageId, "file", SmallItemBytes, guestId: guestId);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Too many items", problem.Title);

        Assert.AreEqual(2, (await GetDatabaseCountsAsync()).ContentCount);
        Assert.AreEqual(2, await CountStoredObjectsAsync());
    }

    [TestMethod]
    public async Task UploadItem_RetryOfExistingItem_DoesNotTripTheItemLimit()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);
        var contentId = Guid.NewGuid();

        await SendUploadItemAsync(packageId, "file", SmallItemBytes, contentId: contentId, guestId: guestId);
        await SendUploadItemAsync(packageId, "file", SmallItemBytes, guestId: guestId);

        // The package is at its cap, but this is an idempotent retry of an
        // existing item, not a new one.
        var retry = await SendUploadItemAsync(
            packageId, "file", SmallItemBytes, contentId: contentId, guestId: guestId);

        var body = await retry.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, retry.StatusCode, body);
        Assert.AreEqual(2, (await GetDatabaseCountsAsync()).ContentCount);
    }
}
