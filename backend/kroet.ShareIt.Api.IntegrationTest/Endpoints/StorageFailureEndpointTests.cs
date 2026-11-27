using System.Net;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;
using kroet.ShareIt.Api.Services.Storage;
using kroet.ShareIt.Api.Storage;
using kroet.ShareIt.Api.Storage.Postgres;
using kroet.ShareIt.Api.Storage.S3;
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
        var packageId = await CreatePackageAsync();

        var storage = Assert.IsInstanceOfType<ControllableStorageHandler>(
            Services.GetRequiredService<IStorageHandler>());
        storage.FailOnStoreCall = 1;

        var response = await SendUploadItemAsync(packageId, "file", [1, 2, 3]);

        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);

        var counts = await GetDatabaseCountsAsync();
        Assert.AreEqual((1, 0), counts, "The package should remain, but the failed item must not.");
        Assert.AreEqual(0, await CountStoredObjectsAsync(), "The failed upload must not leave a blob behind.");
    }

    [TestMethod]
    public async Task UploadItem_AfterAFailedAttempt_CanBeRetriedWithTheSameIds()
    {
        var packageId = await CreatePackageAsync();
        var contentId = Guid.NewGuid();

        var storage = Assert.IsInstanceOfType<ControllableStorageHandler>(
            Services.GetRequiredService<IStorageHandler>());
        storage.FailOnStoreCall = 1;

        var failed = await SendUploadItemAsync(
            packageId, "file", SmallItemBytes, contentId: contentId);
        Assert.AreEqual(HttpStatusCode.InternalServerError, failed.StatusCode);

        storage.FailOnStoreCall = null;
        var retried = await SendUploadItemAsync(
            packageId, "file", SmallItemBytes, contentId: contentId);

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
        var packageId = await CreatePackageAsync(userId: ownerId, maxAccessCount: 10, timeToLiveHours: 24);

        var response = await DeletePackageAsync(packageId, userId: ownerId);

        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Failed to delete package", problem.Title);

        var stored = await Services.GetRequiredService<IContentStorageHandler>()
            .GetPackageAsync(packageId, includeContent: false);
        Assert.IsNotNull(stored);
    }
}

/// <summary>
/// Blob-deletion queue behaviour when a real S3 deletion fails once: package
/// deletion still completes durably and the queued blob is retried later.
/// </summary>
[TestClass]
public sealed class BlobDeletionFailureEndpointTests : ShareItApiTestBase
{
    private ControllableBlobStorageHandler _blobHandler = null!;

    protected override Action<IServiceCollection> ConfigureTestServices => services =>
    {
        services.AddSingleton<IBlobStorageHandler>(sp =>
        {
            _blobHandler = new ControllableBlobStorageHandler(
                ActivatorUtilities.CreateInstance<S3BlobStorageHandler>(sp));
            return _blobHandler;
        });
    };

    [TestMethod]
    public async Task Cleanup_WhenBlobDeletionFails_KeepsQueueEntryAndRetriesIt()
    {
        var ownerId = Guid.NewGuid();
        var (packageId, _) = await CreatePackageWithItemAsync(
            userId: ownerId, maxAccessCount: 10, timeToLiveHours: 24);

        var response = await DeletePackageAsync(packageId, userId: ownerId);
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode, body);

        var contentHandler = Services.GetRequiredService<IContentStorageHandler>();
        Assert.IsNull(
            await contentHandler.GetPackageAsync(packageId, includeContent: false),
            "Metadata deletion must succeed even when blob deletion is deferred.");

        Assert.AreEqual(
            0,
            _blobHandler.DeleteCallCount,
            "The request path must only queue blobs; cleanup owns physical deletion.");
        Assert.AreEqual(1, await CountStoredObjectsAsync(), "Owner deletion leaves the queued object in S3.");
        Assert.HasCount(
            1,
            await contentHandler.GetPendingBlobDeletionsAsync(10),
            "Owner deletion must durably queue the blob.");

        // The first cleanup run retries the deletion, fails, and keeps the job.
        _blobHandler.FailOnDeleteCall = 1;
        await RunCleanupAsync();

        Assert.AreEqual(1, await CountStoredObjectsAsync(), "The failed deletion must leave the object in S3.");
        Assert.HasCount(
            1,
            await contentHandler.GetPendingBlobDeletionsAsync(10),
            "The failed blob deletion must remain queued.");

        // The next cleanup run retries the deletion and completes it.
        await RunCleanupAsync();

        Assert.AreEqual(0, await CountStoredObjectsAsync(), "The retry must remove the object.");
        Assert.IsEmpty(
            await contentHandler.GetPendingBlobDeletionsAsync(10),
            "The retried queue entry must be completed.");
    }
}
