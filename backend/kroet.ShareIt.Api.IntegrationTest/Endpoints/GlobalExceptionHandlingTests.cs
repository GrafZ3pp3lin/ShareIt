using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using kroet.ShareIt.Api.Content.Dtos;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;
using kroet.ShareIt.Api.Storage;
using kroet.ShareIt.Api.Storage.InMemory;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// Endpoint tests for the global exception handling: unhandled endpoint
/// exceptions (e.g. a failing blob read) must be answered with RFC 7807
/// ProblemDetails (HTTP 500) instead of a bare server response.
/// </summary>
[TestClass]
public sealed class GlobalExceptionHandlingTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Passes uploads through to the real in-memory blob store but fails every
    /// download, simulating a broken storage backend at read time.
    /// </summary>
    private sealed class ThrowingOnDownloadBlobHandler : IBlobStorageHandler
    {
        private readonly InMemoryBlobStorageHandler _inner = new();

        public Task<string> UploadAsync(Stream stream, string contentType, string fileName, CancellationToken cancellationToken = default)
            => _inner.UploadAsync(stream, contentType, fileName, cancellationToken);

        public Task<Stream> DownloadAsync(string blobId, CancellationToken cancellationToken = default)
            => throw new FileNotFoundException($"Blob with ID {blobId} not found.");

        public Task<bool> DeleteAsync(string blobId, CancellationToken cancellationToken = default)
            => _inner.DeleteAsync(blobId, cancellationToken);

        public Task<bool> ExistsAsync(string blobId, CancellationToken cancellationToken = default)
            => _inner.ExistsAsync(blobId, cancellationToken);
    }

    [TestMethod]
    public async Task GetPackage_WithFailingBlobRead_ReturnsProblemDetails500()
    {
        var factory = new ShareItApiFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IBlobStorageHandler>(new ThrowingOnDownloadBlobHandler())));
        using var client = factory.CreateClient();

        var guestId = Guid.NewGuid();

        // Create a package with a text item, then read it back; reading the
        // text blob is what fails (inlined text must not leak the exception).
        var packageId = await CreatePackageAsync(client, guestId);
        Assert.AreEqual(HttpStatusCode.OK, (await SendAddTextAsync(client, packageId, "some text", guestId)).StatusCode);

        var response = await GetPackageAsync(client, packageId, guestId);
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode, body);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType, body);
        Assert.IsTrue(body.Contains("Internal server error", StringComparison.Ordinal), body);
        Assert.IsFalse(body.Contains("some text", StringComparison.Ordinal), body);
    }

    // ---------------------------------------------------------------
    // Local helpers (independent of ShareItApiTestBase's own factory)
    // ---------------------------------------------------------------

    private static async Task<Guid> CreatePackageAsync(HttpClient client, Guid guestId)
    {
        using var form = new MultipartFormDataContent();
        // An empty multipart body is a malformed request on the test server;
        // a title keeps the form well-formed.
        form.Add(new StringContent(string.Empty), "Title");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/content/package") { Content = form };
        request.Headers.Add("X-Guest-ID", guestId.ToString());

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, body);

        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("Id").GetGuid();
    }

    private static Task<HttpResponseMessage> SendAddTextAsync(HttpClient client, Guid packageId, string text, Guid guestId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/content/package/{packageId}/text")
        {
            Content = JsonContent.Create(new CreateTextRequest { Text = text }, options: JsonOptions),
        };
        request.Headers.Add("X-Guest-ID", guestId.ToString());
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> GetPackageAsync(HttpClient client, Guid packageId, Guid guestId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/content/package/{packageId}");
        request.Headers.Add("X-Guest-ID", guestId.ToString());
        return client.SendAsync(request);
    }
}
