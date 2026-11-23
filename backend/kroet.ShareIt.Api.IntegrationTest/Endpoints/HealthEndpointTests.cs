using System.Net;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;
using kroet.ShareIt.Api.Models;
using kroet.ShareIt.Api.Storage;
using kroet.ShareIt.Api.Storage.Postgres;
using kroet.ShareIt.Api.Storage.S3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ContentModel = kroet.ShareIt.Api.Models.Content;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// Endpoint tests for the liveness and readiness probes: liveness must stay
/// healthy as long as the process responds, readiness must reflect the
/// backing stores and answer 503 while they are unreachable.
/// </summary>
[TestClass]
public sealed class HealthEndpointTests : ShareItApiTestBase
{
    private sealed class HealthResponse
    {
        public string Status { get; set; } = "";
        public Dictionary<string, HealthEntry> Entries { get; set; } = new();
    }

    private sealed class HealthEntry
    {
        public string Status { get; set; } = "";
        public string? Description { get; set; }
        public double DurationMs { get; set; }
    }

    [TestMethod]
    public async Task Live_ReturnsHealthy()
    {
        var response = await Client.GetAsync("/health/live");
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
        Assert.AreEqual("application/json", response.Content.Headers.ContentType?.MediaType, body);

        var health = await ReadJsonAsync<HealthResponse>(response);
        Assert.AreEqual("Healthy", health.Status, body);
        Assert.AreEqual("Healthy", health.Entries["self"].Status, body);
        CollectionAssert.DoesNotContain(health.Entries.Keys, "content-store");
        CollectionAssert.DoesNotContain(health.Entries.Keys, "blob-store");
    }

    [TestMethod]
    public async Task Ready_WithReachableDependencies_ReturnsHealthy()
    {
        var response = await Client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
        Assert.AreEqual("application/json", response.Content.Headers.ContentType?.MediaType, body);

        var health = await ReadJsonAsync<HealthResponse>(response);
        Assert.AreEqual("Healthy", health.Status, body);
        Assert.AreEqual("Healthy", health.Entries["content-store"].Status, body);
        Assert.AreEqual("Healthy", health.Entries["blob-store"].Status, body);
    }
}

/// <summary>
/// Readiness must degrade to 503 when a backing store is unreachable, while
/// liveness keeps answering healthy because the process itself is fine.
/// </summary>
[TestClass]
public sealed class HealthEndpointUnhealthyDependencyTests : ShareItApiTestBase
{
    /// <summary>
    /// Passes every operation through to the real store but reports the
    /// content metadata store as unreachable to health probes.
    /// </summary>
    private sealed class UnhealthyContentStorageHandler(IContentStorageHandler inner) : IContentStorageHandler
    {
        public Task StorePackageAsync(ContentPackage package, CancellationToken cancellationToken = default)
            => inner.StorePackageAsync(package, cancellationToken);

        public Task StoreContentAsync(ContentModel content, CancellationToken cancellationToken = default)
            => inner.StoreContentAsync(content, cancellationToken);

        public Task<ContentPackage?> GetPackageAsync(Guid packageId, bool includeContent = true, CancellationToken cancellationToken = default)
            => inner.GetPackageAsync(packageId, includeContent, cancellationToken);

        public Task<ContentPackage?> GetPackageShellAsync(Guid packageId, CancellationToken cancellationToken = default)
            => inner.GetPackageShellAsync(packageId, cancellationToken);

        public Task<PackageListPage> ListPackagesAsync(Guid userId, string? cursor, int take, CancellationToken cancellationToken = default)
            => inner.ListPackagesAsync(userId, cursor, take, cancellationToken);

        public Task<ContentModel?> GetContentAsync(Guid contentId, CancellationToken cancellationToken = default)
            => inner.GetContentAsync(contentId, cancellationToken);

        public Task<AccessSessionClaimResult> CreateAccessSessionAsync(AccessSession session, CancellationToken cancellationToken = default)
            => inner.CreateAccessSessionAsync(session, cancellationToken);

        public Task<AccessSession?> GetAccessSessionByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
            => inner.GetAccessSessionByTokenHashAsync(tokenHash, cancellationToken);

        public Task<long> DeleteExpiredAccessSessionsAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
            => inner.DeleteExpiredAccessSessionsAsync(now, cancellationToken);

        public Task<int> DeletePackagesAsync(IEnumerable<Guid> packageIds, CancellationToken cancellationToken = default)
            => inner.DeletePackagesAsync(packageIds, cancellationToken);

        public Task<bool> DeletePackageAsync(Guid packageId, CancellationToken cancellationToken = default)
            => inner.DeletePackageAsync(packageId, cancellationToken);

        public Task<IReadOnlyList<Guid>> GetPackagesToDeleteAsync(CancellationToken cancellationToken = default)
            => inner.GetPackagesToDeleteAsync(cancellationToken);

        public Task<IReadOnlyList<string>> GetPendingBlobDeletionsAsync(int take, CancellationToken cancellationToken = default)
            => inner.GetPendingBlobDeletionsAsync(take, cancellationToken);

        public Task CompleteBlobDeletionAsync(string blobId, CancellationToken cancellationToken = default)
            => inner.CompleteBlobDeletionAsync(blobId, cancellationToken);

        public Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    /// <summary>
    /// Passes every operation through to the real blob store but reports it
    /// as unreachable to health probes.
    /// </summary>
    private sealed class UnhealthyBlobStorageHandler(IBlobStorageHandler inner) : IBlobStorageHandler
    {
        public Task<string> UploadAsync(Stream stream, string contentType, string fileName, CancellationToken cancellationToken = default)
            => inner.UploadAsync(stream, contentType, fileName, cancellationToken);

        public Task<Stream> DownloadAsync(string blobId, CancellationToken cancellationToken = default)
            => inner.DownloadAsync(blobId, cancellationToken);

        public Task DeleteAsync(string blobId, CancellationToken cancellationToken = default)
            => inner.DeleteAsync(blobId, cancellationToken);

        public Task InitializeAsync(CancellationToken cancellationToken = default)
            => inner.InitializeAsync(cancellationToken);

        public Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    protected override Action<IServiceCollection> ConfigureTestServices => services =>
    {
        services.AddSingleton<IBlobStorageHandler>(sp => new UnhealthyBlobStorageHandler(
            new S3BlobStorageHandler(
                sp.GetRequiredService<IOptions<S3BlobStorageSettings>>(),
                sp.GetRequiredService<ILogger<S3BlobStorageHandler>>())));
        services.AddSingleton<IContentStorageHandler>(sp => new UnhealthyContentStorageHandler(
            sp.GetRequiredService<SqlContentStorageHandler>()));
    };

    [TestMethod]
    public async Task Live_WithUnreachableDependencies_StaysHealthy()
    {
        var response = await Client.GetAsync("/health/live");
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);

        var health = await ReadJsonAsync<HealthResponse>(response);
        Assert.AreEqual("Healthy", health.Status, body);
    }

    [TestMethod]
    public async Task Ready_WithUnreachableDependencies_ReturnsServiceUnavailable()
    {
        var response = await Client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode, body);

        var health = await ReadJsonAsync<HealthResponse>(response);
        Assert.AreEqual("Unhealthy", health.Status, body);
        Assert.AreEqual("Unhealthy", health.Entries["content-store"].Status, body);
        Assert.AreEqual("Unhealthy", health.Entries["blob-store"].Status, body);
    }

    private sealed class HealthResponse
    {
        public string Status { get; set; } = "";
        public Dictionary<string, HealthEntry> Entries { get; set; } = new();
    }

    private sealed class HealthEntry
    {
        public string Status { get; set; } = "";
        public string? Description { get; set; }
        public double DurationMs { get; set; }
    }
}
