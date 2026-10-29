using System.Net;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;
using kroet.ShareIt.Api.Services.Storage;
using kroet.ShareIt.Api.Storage;
using kroet.ShareIt.Api.Storage.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// Endpoint rollback behaviour when a real storage write fails halfway
/// through a multi-item request.
/// </summary>
[TestClass]
public sealed class RollbackEndpointTests : ShareItApiTestBase
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
    public async Task CreatePackage_WhenStorageFailsMidUpload_RollsBackPackageAndStoredObjects()
    {
        var storage = Assert.IsInstanceOfType<ControllableStorageHandler>(
            Services.GetRequiredService<IStorageHandler>());
        storage.FailOnStoreCall = 2;

        var response = await SendCreatePackageAsync(
            guestId: Guid.NewGuid(),
            files: [([1, 2, 3], "one.bin"), ([4, 5, 6], "two.bin")]);

        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);

        var counts = await GetDatabaseCountsAsync();
        Assert.AreEqual((0, 0), counts, "The package and all content rows should be rolled back.");
        Assert.AreEqual(0, await CountStoredObjectsAsync(), "All uploaded blobs should be rolled back.");
    }

    [TestMethod]
    public async Task UploadFilesToPackage_WhenStorageFailsMidUpload_RollsBackOnlyNewContent()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId, fileBytes: [9, 9, 9]);

        var storage = Assert.IsInstanceOfType<ControllableStorageHandler>(
            Services.GetRequiredService<IStorageHandler>());
        storage.FailOnStoreCall = 3;

        var response = await SendUploadFilesAsync(
            packageId,
            [([1, 2, 3], "one.bin"), ([4, 5, 6], "two.bin")],
            guestId);

        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);

        var counts = await GetDatabaseCountsAsync();
        Assert.AreEqual((1, 1), counts, "Only the pre-existing package and content should remain.");
        Assert.AreEqual(1, await CountStoredObjectsAsync(), "Only the pre-existing blob should remain.");

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);
        Assert.HasCount(1, details.Items);
        Assert.AreEqual("hello.txt", details.Items[0].FileName);
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
        var packageId = await CreatePackageAsync(userId: ownerId, fileBytes: [1, 2, 3]);

        var response = await DeletePackageAsync(packageId, userId: ownerId);

        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Failed to delete package", problem.Title);

        var stored = await Services.GetRequiredService<IContentStorageHandler>()
            .GetPackageAsync(packageId, includeContent: false);
        Assert.IsNotNull(stored);
    }
}
