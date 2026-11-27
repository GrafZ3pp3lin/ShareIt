using System.Net;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;
using kroet.ShareIt.Api.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// Cleanup lifecycle against the real Postgres and RustFS stores: an exhausted
/// package stays available — with its blobs — until the retention window
/// elapses, while expiry remains a hard boundary.
/// </summary>
[TestClass]
public sealed class CleanupLifecycleEndpointTests : ShareItApiTestBase
{
    [TestMethod]
    public async Task Cleanup_RetainsExhaustedPackageUntilRetentionElapses_ThenDeletesRowsAndBlobs()
    {
        var (packageId, _) = await CreatePackageWithItemAsync(
            bytes: LargeItemBytes, maxAccessCount: 1);

        var details = await GetPackageDetailsAsync(packageId);
        Assert.AreEqual(1, details.Package.CurrentAccessCount);

        // Exhausted: cleanup must retain the package so its access session
        // stays usable, and its blob must survive.
        await RunCleanupAsync();
        var retained = await Services.GetRequiredService<IContentStorageHandler>()
            .GetPackageShellAsync(packageId);
        Assert.IsNotNull(retained, "An exhausted package must stay available during the retention window.");
        Assert.AreEqual(1, await CountStoredObjectsAsync(), "The blob must survive while the package is retained.");

        // The session issued by the final access must still work after cleanup.
        var download = await SendDownloadAsync(
            DownloadUrl(packageId, details.Items.Single().Id),
            details.AccessSession.Token);
        Assert.AreEqual(
            HttpStatusCode.OK,
            download.StatusCode,
            "Cleanup must not invalidate the access session issued by the final access.");
        Assert.AreSequenceEqual(LargeItemBytes, await download.Content.ReadAsByteArrayAsync());

        // After the retention window the package becomes cleanup-eligible.
        TimeProvider.SetUtcNow(TimeProvider.GetUtcNow().AddMinutes(31));
        await RunCleanupAsync();

        Assert.IsNull(await Services.GetRequiredService<IContentStorageHandler>().GetPackageShellAsync(packageId));
        Assert.AreEqual(0, await CountStoredObjectsAsync(), "Cleanup must delete the blobs of a retained-then-expired package.");
        Assert.IsEmpty(await Services.GetRequiredService<IContentStorageHandler>().GetPendingBlobDeletionsAsync(10));
    }

    [TestMethod]
    public async Task Cleanup_DeletesExpiredPackageImmediately_EvenWithinRetention()
    {
        var packageId = await CreatePackageAsync(timeToLiveHours: 1);
        var upload = await SendUploadItemAsync(packageId, "file", SmallItemBytes);
        var uploadBody = await upload.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, upload.StatusCode, uploadBody);

        TimeProvider.SetUtcNow(TimeProvider.GetUtcNow().AddHours(2));

        await RunCleanupAsync();

        Assert.IsNull(
            await Services.GetRequiredService<IContentStorageHandler>().GetPackageShellAsync(packageId),
            "Expiry is a hard boundary: cleanup deletes expired packages without retention.");
        Assert.AreEqual(0, await CountStoredObjectsAsync());
        Assert.IsEmpty(await Services.GetRequiredService<IContentStorageHandler>().GetPendingBlobDeletionsAsync(10));
    }
}
