using System.Net;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;
using kroet.ShareIt.Api.Storage;
using kroet.ShareIt.Api.Storage.S3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// Endpoint tests for the global exception handling: unhandled endpoint
/// exceptions (e.g. a failing blob read) must be answered with RFC 7807
/// ProblemDetails (HTTP 500) instead of a bare server response.
/// </summary>
[TestClass]
public sealed class GlobalExceptionHandlingTests : ShareItApiTestBase
{
    /// <summary>
    /// Passes uploads through to the real S3/RustFS blob store but fails every
    /// download, simulating a broken storage backend at read time.
    /// </summary>
    private sealed class ThrowingOnDownloadBlobHandler(IBlobStorageHandler inner) : IBlobStorageHandler
    {
        public Task<string> UploadAsync(Stream stream, string contentType, string fileName, CancellationToken cancellationToken = default)
            => inner.UploadAsync(stream, contentType, fileName, cancellationToken);

        public Task InitializeAsync(CancellationToken cancellationToken = default)
            => inner.InitializeAsync(cancellationToken);

        public Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
            => inner.CheckHealthAsync(cancellationToken);

        public Task<Stream> DownloadAsync(string blobId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException($"Simulated failure reading blob {blobId}.");

        public Task DeleteAsync(string blobId, CancellationToken cancellationToken = default)
            => inner.DeleteAsync(blobId, cancellationToken);
    }

    protected override Action<IServiceCollection> ConfigureTestServices => services =>
    {
        services.AddSingleton<IBlobStorageHandler>(sp => new ThrowingOnDownloadBlobHandler(
            new S3BlobStorageHandler(
                sp.GetRequiredService<IOptions<S3BlobStorageSettings>>(),
                sp.GetRequiredService<ILogger<S3BlobStorageHandler>>())));
    };

    [TestMethod]
    public async Task DownloadContent_WithFailingBlobRead_ReturnsProblemDetails500()
    {
        var guestId = Guid.NewGuid();

        var packageId = await CreatePackageAsync(guestId: guestId);
        var upload = await SendUploadItemAsync(packageId, "text", "some text"u8.ToArray(), guestId: guestId);
        Assert.AreEqual(HttpStatusCode.OK, upload.StatusCode);

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);
        var item = details.Items.Single();

        var response = await SendDownloadAsync(
            DownloadUrl(packageId, item.Id),
            details.AccessSession.Token,
            guestId);
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode, body);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType, body);
        Assert.IsTrue(body.Contains("Internal server error", StringComparison.Ordinal), body);
        Assert.IsFalse(body.Contains("some text", StringComparison.Ordinal), body);
    }
}
