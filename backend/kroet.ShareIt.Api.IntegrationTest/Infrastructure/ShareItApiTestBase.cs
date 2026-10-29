using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Dapper;
using kroet.ShareIt.Api.Content.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace kroet.ShareIt.Api.IntegrationTest.Infrastructure;

/// <summary>
/// Shared plumbing for endpoint tests. Every test method gets a fresh API
/// instance backed by the real Postgres and RustFS containers, with the
/// database reset and a unique S3 bucket per test so tests are isolated.
/// </summary>
public abstract class ShareItApiTestBase
{
    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private ShareItApiFactory _factory = null!;
    private AmazonS3Client _s3Client = null!;

    protected HttpClient Client { get; private set; } = null!;

    protected string BucketName { get; private set; } = null!;

    protected MutableTimeProvider TimeProvider { get; private set; } = null!;

    protected PostgresIntegrationDatabase Database => EndpointTestStorage.Database;

    protected AmazonS3Client S3Client => _s3Client;

    protected IServiceProvider Services => _factory.Services;

    /// <summary>
    /// Configuration overrides applied on top of the application defaults.
    /// </summary>
    protected virtual IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>();

    /// <summary>
    /// Optional service overrides applied after the real storage registrations.
    /// </summary>
    protected virtual Action<IServiceCollection>? ConfigureTestServices => null;

    [TestInitialize]
    public async Task StartApplication()
    {
        await EndpointTestStorage.EnsureStartedAsync();
        await EndpointTestStorage.ResetDatabaseAsync();

        BucketName = $"shareit-e2e-{Guid.NewGuid():N}";
        TimeProvider = new MutableTimeProvider(DateTimeOffset.UtcNow);

        var settings = new Dictionary<string, string?>(Settings)
        {
            ["ContentStorage:PostgreSqlDb:ConnectionString"] = Database.ConnectionString,
            ["ContentStorage:PostgreSqlDb:AutoInitializeSchema"] = "false",
            ["BlobStorage:S3:AccessKeyId"] = EndpointTestStorage.RustFs.AccessKeyId,
            ["BlobStorage:S3:SecretAccessKey"] = EndpointTestStorage.RustFs.SecretAccessKey,
            ["BlobStorage:S3:ServiceUrl"] = EndpointTestStorage.RustFs.ServiceUrl,
            ["BlobStorage:S3:BucketName"] = BucketName
        };

        _factory = new ShareItApiFactory(
            settings,
            useInMemoryStorage: false,
            configureTestServices: services =>
            {
                services.AddSingleton<TimeProvider>(TimeProvider);
                ConfigureTestServices?.Invoke(services);
            });

        Client = _factory.CreateClient();
        _s3Client = EndpointTestStorage.RustFs.CreateClient();
    }

    [TestCleanup]
    public void StopApplication()
    {
        Client?.Dispose();
        _factory?.Dispose();
        _s3Client?.Dispose();
    }

    /// <summary>
    /// Sends a multipart POST to the package creation endpoint.
    /// </summary>
    protected async Task<HttpResponseMessage> SendCreatePackageAsync(
        byte[]? fileBytes = null,
        string fileName = "hello.txt",
        string? title = null,
        int? maxDownloadCount = 1,
        bool? isPrivate = null,
        Guid? guestId = null,
        Guid? userId = null,
        int? timeToLiveHours = 1,
        string[]? texts = null,
        (byte[] Bytes, string FileName)[]? files = null)
    {
        using var form = new MultipartFormDataContent();
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
        if (timeToLiveHours is not null)
        {
            form.Add(new StringContent(timeToLiveHours.Value.ToString()), "TimeToLiveHours");
        }
        if (texts is not null)
        {
            foreach (var text in texts)
            {
                form.Add(new StringContent(text), "Texts");
            }
        }

        foreach (var file in files ?? (fileBytes is null ? [] : [(fileBytes, fileName)]))
        {
            var fileContent = new ByteArrayContent(file.Bytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            form.Add(fileContent, "Files", file.FileName);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/content/package") { Content = form };
        AddIdentity(request, guestId, userId);
        return await Client.SendAsync(request);
    }

    /// <summary>
    /// Creates a package via POST /package and asserts it succeeded.
    /// Returns the created package ID.
    /// </summary>
    protected async Task<Guid> CreatePackageAsync(
        byte[]? fileBytes = null,
        string fileName = "hello.txt",
        string? title = null,
        int? maxDownloadCount = 1,
        bool? isPrivate = null,
        Guid? guestId = null,
        Guid? userId = null,
        int? timeToLiveHours = 1,
        string[]? texts = null,
        (byte[] Bytes, string FileName)[]? files = null)
    {
        var response = await SendCreatePackageAsync(
            fileBytes, fileName, title, maxDownloadCount, isPrivate, guestId, userId, timeToLiveHours, texts, files);
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
        => SendUploadFilesAsync(packageId, [(fileBytes, fileName)], guestId, userId);

    /// <summary>
    /// Sends a multipart POST of one or more files to an existing package.
    /// </summary>
    protected async Task<HttpResponseMessage> SendUploadFilesAsync(
        Guid packageId,
        (byte[] Bytes, string FileName)[] files,
        Guid? guestId = null,
        Guid? userId = null)
    {
        using var form = new MultipartFormDataContent();
        foreach (var file in files)
        {
            var fileContent = new ByteArrayContent(file.Bytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            form.Add(fileContent, "files", file.FileName);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/content/package/{packageId}/upload") { Content = form };
        AddIdentity(request, guestId, userId);
        return await Client.SendAsync(request);
    }

    /// <summary>
    /// Sends a multipart POST without any file parts to exercise the
    /// "no files provided" endpoint branch.
    /// </summary>
    protected async Task<HttpResponseMessage> SendUploadWithoutFilesAsync(
        Guid packageId,
        Guid? guestId = null,
        Guid? userId = null)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent("noop"), "Dummy" }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/content/package/{packageId}/upload") { Content = form };
        AddIdentity(request, guestId, userId);
        return await Client.SendAsync(request);
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

    /// <summary>
    /// Fetches a content item via its download token.
    /// </summary>
    protected Task<HttpResponseMessage> SendDownloadAsync(string downloadUrl, string? token, Guid? guestId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        if (token is not null)
        {
            request.Headers.Add("X-File-Token", token);
        }
        if (guestId.HasValue)
        {
            request.Headers.Add("X-Guest-ID", guestId.Value.ToString());
        }
        return Client.SendAsync(request);
    }

    protected Task<HttpResponseMessage> DeletePackageAsync(Guid packageId, Guid? guestId = null, Guid? userId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/content/packages/{packageId}");
        AddIdentity(request, guestId, userId);
        return Client.SendAsync(request);
    }

    /// <summary>
    /// Counts all objects in the test's RustFS bucket.
    /// </summary>
    protected async Task<int> CountStoredObjectsAsync()
    {
        var response = await S3Client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = BucketName });
        return response.S3Objects?.Count ?? 0;
    }

    /// <summary>
    /// Reads package/content row counts directly from the real Postgres database.
    /// </summary>
    protected async Task<(int PackageCount, int ContentCount)> GetDatabaseCountsAsync()
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync();

        var packageCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM content_packages");
        var contentCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM contents");

        return (packageCount, contentCount);
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
