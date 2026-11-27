using System.Net;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;
using kroet.ShareIt.Api.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// Package expiry and empty-package endpoint behaviour against the real
/// Postgres and RustFS stores.
/// </summary>
[TestClass]
public sealed class PackageLifecycleEndpointTests : ShareItApiTestBase
{
    [TestMethod]
    public async Task GetPackage_WhenExpired_Returns410()
    {
        var packageId = await CreatePackageAsync(timeToLiveHours: 1);
        await SendUploadItemAsync(packageId, "file", SmallItemBytes);

        TimeProvider.SetUtcNow(TimeProvider.GetUtcNow().AddHours(2));

        var response = await GetPackageAsync(packageId);

        Assert.AreEqual(HttpStatusCode.Gone, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Package expired", problem.Title);
    }

    [TestMethod]
    public async Task GetPackage_WhenPackageHasNoContent_Returns410WithoutChargingAnAccess()
    {
        var packageId = await CreatePackageAsync(maxAccessCount: 1);

        var response = await GetPackageAsync(packageId);

        Assert.AreEqual(HttpStatusCode.Gone, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.AreEqual("Package is empty", problem.Title);

        // The empty read must not consume the package's only access.
        var shell = await Services.GetRequiredService<IContentStorageHandler>()
            .GetPackageShellAsync(packageId);
        Assert.IsNotNull(shell);
        Assert.AreEqual(0, shell.CurrentAccessCount, "An empty package must not charge an access.");

        // After a successful upload the same link must still open and then
        // consume exactly one access.
        var upload = await SendUploadItemAsync(packageId, "file", SmallItemBytes);
        var uploadBody = await upload.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, upload.StatusCode, uploadBody);

        var details = await GetPackageDetailsAsync(packageId);
        Assert.AreEqual(1, details.Package.CurrentAccessCount);
    }
}
