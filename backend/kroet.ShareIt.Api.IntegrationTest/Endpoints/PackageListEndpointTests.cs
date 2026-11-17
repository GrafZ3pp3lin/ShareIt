using System.Net;
using kroet.ShareIt.Api.Content.Dtos;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// The owner's links list: authenticated-only, newest first, keyset
/// paginated, and free of names (only timestamps, counters and sizes).
/// </summary>
[TestClass]
public sealed class PackageListEndpointTests : ShareItApiTestBase
{
    [TestMethod]
    public async Task ListPackages_WithoutIdentity_Returns401Challenge()
    {
        var response = await Client.GetAsync("/api/v1/content/packages");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListPackages_AsGuest_Returns401Challenge()
    {
        var response = await ListPackagesAsync(guestId: Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListPackages_AsOwner_ReturnsOnlyOwnPackagesNewestFirst()
    {
        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        TimeProvider.UtcNow = TimeProvider.UtcNow.AddHours(-2);
        var oldest = await CreatePackageAsync(userId: ownerId, maxAccessCount: 10, timeToLiveHours: 24);

        TimeProvider.UtcNow = TimeProvider.UtcNow.AddHours(1);
        var middle = await CreatePackageAsync(userId: ownerId, maxAccessCount: 10, timeToLiveHours: 24);

        TimeProvider.UtcNow = TimeProvider.UtcNow.AddHours(1);
        var newest = await CreatePackageAsync(userId: ownerId, maxAccessCount: 10, timeToLiveHours: 24);

        await CreatePackageAsync(userId: otherId, maxAccessCount: 10, timeToLiveHours: 24);

        var response = await ListPackagesAsync(userId: ownerId);
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);

        var page = await ReadJsonAsync<ContentPackageListResponse>(response);
        CollectionAssert.AreEqual(
            new[] { newest, middle, oldest },
            page.Items.Select(item => item.Id).ToArray());
        Assert.IsNull(page.NextCursor);

        // Rows carry no names and no owner id.
        Assert.IsFalse(body.Contains("Title", StringComparison.OrdinalIgnoreCase), body);
        Assert.IsFalse(body.Contains("UserId", StringComparison.OrdinalIgnoreCase), body);
    }

    [TestMethod]
    public async Task ListPackages_ReportsItemCountTotalSizeAndRemainingAccesses()
    {
        var ownerId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(
            userId: ownerId, maxAccessCount: 3, timeToLiveHours: 24);

        await SendUploadItemAsync(packageId, "file", new byte[10], guestId: null, userId: ownerId);
        TimeProvider.UtcNow = TimeProvider.UtcNow.AddMinutes(1);
        await SendUploadItemAsync(packageId, "text", new byte[20], guestId: null, userId: ownerId);

        var page = await ReadJsonAsync<ContentPackageListResponse>(await ListPackagesAsync(userId: ownerId));

        var row = page.Items.Single();
        Assert.AreEqual(packageId, row.Id);
        Assert.AreEqual(2, row.ItemCount);
        Assert.AreEqual(30, row.TotalSize);
        Assert.AreEqual(3, row.MaxAccessCount);
        Assert.AreEqual(0, row.CurrentAccessCount);
        Assert.AreEqual(3, row.RemainingAccesses);
        Assert.IsFalse(row.IsExpired);
        Assert.IsFalse(row.IsProtected);
    }

    [TestMethod]
    public async Task ListPackages_ReportsWhetherThePackageIsProtected()
    {
        var ownerId = Guid.NewGuid();
        var unprotected = await CreatePackageAsync(userId: ownerId, maxAccessCount: 10, timeToLiveHours: 24);
        TimeProvider.UtcNow = TimeProvider.UtcNow.AddMinutes(1);
        var protectedPackage = await CreatePackageAsync(
            userId: ownerId, maxAccessCount: 10, timeToLiveHours: 24, unlockVerifier: Verifier());

        var page = await ReadJsonAsync<ContentPackageListResponse>(await ListPackagesAsync(userId: ownerId));

        Assert.HasCount(2, page.Items);
        var protectedRow = page.Items.Single(item => item.Id == protectedPackage);
        Assert.IsTrue(protectedRow.IsProtected);
        var unprotectedRow = page.Items.Single(item => item.Id == unprotected);
        Assert.IsFalse(unprotectedRow.IsProtected);
    }

    [TestMethod]
    public async Task ListPackages_UsesKeysetPagination()
    {
        var ownerId = Guid.NewGuid();

        var created = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            TimeProvider.UtcNow = TimeProvider.UtcNow.AddMinutes(1);
            created.Add(await CreatePackageAsync(userId: ownerId, maxAccessCount: 10, timeToLiveHours: 24));
        }

        // Newest first: [2], [1], [0].
        var firstPage = await ReadJsonAsync<ContentPackageListResponse>(
            await ListPackagesAsync(userId: ownerId, take: 2));

        Assert.HasCount(2, firstPage.Items);
        CollectionAssert.AreEqual(
            new[] { created[2], created[1] },
            firstPage.Items.Select(item => item.Id).ToArray());
        Assert.IsNotNull(firstPage.NextCursor);

        var secondPage = await ReadJsonAsync<ContentPackageListResponse>(
            await ListPackagesAsync(userId: ownerId, take: 2, cursor: firstPage.NextCursor));

        Assert.HasCount(1, secondPage.Items);
        Assert.AreEqual(created[0], secondPage.Items[0].Id);
        Assert.IsNull(secondPage.NextCursor);
    }

    [TestMethod]
    public async Task ListPackages_MarksExpiredPackages()
    {
        var ownerId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(
            userId: ownerId, maxAccessCount: 10, timeToLiveHours: 1);

        TimeProvider.UtcNow = TimeProvider.UtcNow.AddHours(2);

        var page = await ReadJsonAsync<ContentPackageListResponse>(await ListPackagesAsync(userId: ownerId));

        var row = page.Items.Single();
        Assert.AreEqual(packageId, row.Id);
        Assert.IsTrue(row.IsExpired);
    }

    [TestMethod]
    public async Task ListPackages_WithNoPackages_ReturnsEmptyPage()
    {
        var page = await ReadJsonAsync<ContentPackageListResponse>(
            await ListPackagesAsync(userId: Guid.NewGuid()));

        Assert.IsEmpty(page.Items);
        Assert.IsNull(page.NextCursor);
    }
}
