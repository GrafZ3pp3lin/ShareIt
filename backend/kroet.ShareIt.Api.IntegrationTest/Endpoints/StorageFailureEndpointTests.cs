using System.Net;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;
using kroet.ShareIt.Api.Services.Storage;
using kroet.ShareIt.Api.Storage;
using kroet.ShareIt.Api.Storage.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// Endpoint behaviour when a real storage write fails. A single-item upload
/// leaves no partial row or orphaned blob behind.
/// </summary>
[TestClass]
public sealed class StorageFailureEndpointTests : ShareItApiTestBase
{
    protected override Action<IServiceCollection> ConfigureTestServices => services =>
    {
        services.AddSingleton<IStorageHandler>(sp => new ControllableStorageHandler(
            new StorageHandler(
                sp.GetRequiredService<IContentStorageHandler>(),
                sp.GetRequiredService<IBlobStorageHandler>(),
                sp.GetRequiredService<ILogger<StorageHandler>>())));
    };

    [TestMethod]
    public async Task UploadItem_WhenStorageFails_LeavesNoContentRowOrObject()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);

        var storage = Assert.IsInstanceOfType<ControllableStorageHandler>(
            Services.GetRequiredService<IStorageHandler>());
        storage.FailOnStoreCall = 1;

        var response = await SendUploadItemAsync(packageId, "file", [1, 2, 3], guestId: guestId);

        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);

        var counts = await GetDatabaseCountsAsync();
        Assert.AreEqual((1, 0), counts, "The package should remain, but the failed item must not.");
        Assert.AreEqual(0, await CountStoredObjectsAsync(), "The failed upload must not leave a blob behind.");
    }

    [TestMethod]
    public async Task UploadItem_AfterAFailedAttempt_CanBeRetriedWithTheSameIds()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);
        var contentId = Guid.NewGuid();

        var storage = Assert.IsInstanceOfType<ControllableStorageHandler>(
            Services.GetRequiredService<IStorageHandler>());
        storage.FailOnStoreCall = 1;

        var failed = await SendUploadItemAsync(
            packageId, "file", SmallItemBytes, contentId: contentId, guestId: guestId);
        Assert.AreEqual(HttpStatusCode.InternalServerError, failed.StatusCode);

        storage.FailOnStoreCall = null;
        var retried = await SendUploadItemAsync(
            packageId, "file", SmallItemBytes, contentId: contentId, guestId: guestId);

        var body = await retried.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, retried.StatusCode, body);
        Assert.AreEqual((1, 1), await GetDatabaseCountsAsync());
        Assert.AreEqual(1, await CountStoredObjectsAsync());
    }
}

/// <summary>
/// Endpoint behaviour when the content store fails to delete a package.
/// </summary>
[TestClass]
public sealed class DeleteFailureEndpointTests : ShareItApiTestBase
{
    protected override Action<IServiceCollection> ConfigureTestServices => services =>
    {
        services.AddSingleton<IContentStorageHandler>(sp =>
            new DelegatingContentStorageHandler(
                ActivatorUtilities.CreateInstance<SqlContentStorageHandler>(sp),
                deletePackage: (_, _) => Task.FromResult(false)));
    };

    [TestMethod]
    public async Task DeletePackage_WhenStorageDeleteFails_Returns500AndKeepsPackage()
    {
        var ownerId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(userId: ownerId, maxDownloadCount: 10, timeToLiveHours: 24);

        var response = await DeletePackageAsync(packageId, userId: ownerId);

        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Failed to delete package", problem.Title);

        var stored = await Services.GetRequiredService<IContentStorageHandler>()
            .GetPackageAsync(packageId, includeContent: false);
        Assert.IsNotNull(stored);
    }
}
