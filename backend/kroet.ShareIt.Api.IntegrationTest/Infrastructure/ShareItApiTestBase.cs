using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using kroet.ShareIt.Api.Content.Dtos;

namespace kroet.ShareIt.Api.IntegrationTest.Infrastructure;

/// <summary>
/// Shared plumbing for endpoint tests. Every test method gets a fresh, fully
/// isolated API instance (its own in-memory storage) so tests cannot see
/// each other's packages.
/// </summary>
public abstract class ShareItApiTestBase
{
    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private ShareItApiFactory _factory = null!;

    protected HttpClient Client { get; private set; } = null!;

    /// <summary>
    /// Configuration overrides applied on top of the application defaults.
    /// </summary>
    protected virtual IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>();

    [TestInitialize]
    public void StartApplication()
    {
        _factory = new ShareItApiFactory(Settings);
        Client = _factory.CreateClient();
    }

    [TestCleanup]
    public void StopApplication()
    {
        Client.Dispose();
        _factory.Dispose();
    }

    /// <summary>
    /// Sends a multipart POST to the package creation endpoint.
    /// </summary>
    protected Task<HttpResponseMessage> SendCreatePackageAsync(
        byte[]? fileBytes = null,
        string fileName = "hello.txt",
        string? title = null,
        int? maxDownloadCount = null,
        bool? isPrivate = null,
        Guid? guestId = null,
        Guid? userId = null)
    {
        var form = new MultipartFormDataContent();
        if (title is not null)
        {
            form.Add(new StringContent(title), "Title");
        }
        if (maxDownloadCount is not null)
        {
            form.Add(new StringContent(maxDownloadCount.Value.ToString()), "MaxDownloadCount");
        }
        if (isPrivate is not null)
        {
            form.Add(new StringContent(isPrivate.Value ? "true" : "false"), "IsPrivate");
        }
        if (fileBytes is not null)
        {
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            form.Add(fileContent, "Files", fileName);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/content/package") { Content = form };
        AddIdentity(request, guestId, userId);
        return Client.SendAsync(request);
    }

    /// <summary>
    /// Creates a package via POST /package and asserts it succeeded.
    /// Returns the created package ID.
    /// </summary>
    protected async Task<Guid> CreatePackageAsync(
        byte[]? fileBytes = null,
        string fileName = "hello.txt",
        string? title = null,
        int? maxDownloadCount = null,
        bool? isPrivate = null,
        Guid? guestId = null,
        Guid? userId = null)
    {
        var response = await SendCreatePackageAsync(fileBytes, fileName, title, maxDownloadCount, isPrivate, guestId, userId);
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, body);

        var package = await ReadJsonAsync<ContentPackageResponse>(response);
        return package.Id;
    }

    /// <summary>
    /// Sends a multipart POST of a single file to an existing package.
    /// </summary>
    protected Task<HttpResponseMessage> SendUploadFilesAsync(
        Guid packageId,
        byte[] fileBytes,
        string fileName = "hello.txt",
        Guid? guestId = null,
        Guid? userId = null)
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(fileContent, "files", fileName);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/content/package/{packageId}/upload") { Content = form };
        AddIdentity(request, guestId, userId);
        return Client.SendAsync(request);
    }

    protected Task<HttpResponseMessage> SendAddTextAsync(Guid packageId, string text, Guid? guestId = null, Guid? userId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/content/package/{packageId}/text")
        {
            Content = JsonContent.Create(new CreateTextRequest { Text = text }, options: JsonOptions),
        };
        AddIdentity(request, guestId, userId);
        return Client.SendAsync(request);
    }

    protected Task<HttpResponseMessage> GetPackageAsync(Guid packageId, Guid? guestId = null, Guid? userId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/content/package/{packageId}");
        AddIdentity(request, guestId, userId);
        return Client.SendAsync(request);
    }

    /// <summary>
    /// Fetches a package and asserts the request succeeded.
    /// </summary>
    protected async Task<ContentPackageDetailsResponse> GetPackageDetailsAsync(Guid packageId, Guid? guestId = null, Guid? userId = null)
    {
        var response = await GetPackageAsync(packageId, guestId, userId);
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);

        return await ReadJsonAsync<ContentPackageDetailsResponse>(response);
    }

    protected Task<HttpResponseMessage> DeletePackageAsync(Guid packageId, Guid? guestId = null, Guid? userId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/content/packages/{packageId}");
        AddIdentity(request, guestId, userId);
        return Client.SendAsync(request);
    }

    protected static void AddIdentity(HttpRequestMessage request, Guid? guestId, Guid? userId)
    {
        if (userId.HasValue)
        {
            request.Headers.Add(TestAuthHandler.UserIdHeader, userId.Value.ToString());
        }
        if (guestId.HasValue)
        {
            request.Headers.Add("X-Guest-ID", guestId.Value.ToString());
        }
    }

    protected static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions)
               ?? throw new AssertFailedException($"Expected a {typeof(T).Name} in the response body but got none.");
    }

    protected static async Task<ProblemBody> ReadProblemAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;

        var status = root.TryGetProperty("status", out var statusElement) && statusElement.TryGetInt32(out var parsedStatus)
            ? parsedStatus
            : (int)response.StatusCode;
        var title = root.TryGetProperty("title", out var titleElement)
            ? titleElement.GetString() ?? string.Empty
            : string.Empty;
        var detail = root.TryGetProperty("detail", out var detailElement) ? detailElement.GetString() : null;

        return new ProblemBody(status, title, detail);
    }

    protected sealed record ProblemBody(int Status, string Title, string? Detail);
}
