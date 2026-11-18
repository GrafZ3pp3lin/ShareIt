using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Dapper;
using kroet.ShareIt.Api.Content.Dtos;
using kroet.ShareIt.Api.Services.Cleanup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

    protected static readonly byte[] SmallItemBytes = "hello world"u8.ToArray();

    /// <summary>
    /// Larger than the default 16 KB inline payload limit, so this item is
    /// always served through the package access session.
    /// </summary>
    protected static readonly byte[] LargeItemBytes = new byte[20 * 1024];

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
    /// JSON body for POST /package.
    /// </summary>
    protected static CreatePackageRequest PackageRequest(
        Guid? packageId = null,
        int? maxAccessCount = 1,
        bool? isPrivate = null,
        int? timeToLiveHours = 1,
        string? unlockVerifier = null)
        => new()
        {
            Id = packageId ?? Guid.NewGuid(),
            UnlockVerifier = unlockVerifier,
            MaxAccessCount = maxAccessCount,
            TimeToLiveHours = timeToLiveHours,
            IsPrivate = isPrivate
        };

    /// <summary>
    /// Sends a JSON POST to the package creation endpoint.
    /// </summary>
    protected Task<HttpResponseMessage> SendCreatePackageAsync(
        Guid? packageId = null,
        int? maxAccessCount = 1,
        bool? isPrivate = null,
        Guid? guestId = null,
        Guid? userId = null,
        int? timeToLiveHours = 1,
        string? unlockVerifier = null)
        => SendCreatePackageAsync(
            PackageRequest(packageId, maxAccessCount, isPrivate, timeToLiveHours, unlockVerifier),
            guestId,
            userId);

    protected async Task<HttpResponseMessage> SendCreatePackageAsync(
        CreatePackageRequest request,
        Guid? guestId = null,
        Guid? userId = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/content/package")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        AddIdentity(message, guestId, userId);
        return await Client.SendAsync(message);
    }

    /// <summary>
    /// Creates a package via POST /package and asserts it succeeded.
    /// Returns the created package ID.
    /// </summary>
    protected async Task<Guid> CreatePackageAsync(
        Guid? packageId = null,
        int? maxAccessCount = 1,
        bool? isPrivate = null,
        Guid? guestId = null,
        Guid? userId = null,
        int? timeToLiveHours = 1,
        string? unlockVerifier = null)
    {
        var response = await SendCreatePackageAsync(
            packageId, maxAccessCount, isPrivate, guestId, userId, timeToLiveHours, unlockVerifier);
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, body);

        var package = await ReadJsonAsync<ContentPackageResponse>(response);
        return package.Id;
    }

    /// <summary>
    /// Creates a package and uploads one item into it.
    /// </summary>
    protected async Task<(Guid PackageId, Guid ContentId)> CreatePackageWithItemAsync(
        string kind = "file",
        byte[]? bytes = null,
        string meta = "bWV0YQ",
        Guid? packageId = null,
        Guid? contentId = null,
        int? maxAccessCount = 1,
        bool? isPrivate = null,
        Guid? guestId = null,
        Guid? userId = null,
        int? timeToLiveHours = 1,
        string? unlockVerifier = null)
    {
        var createdPackageId = await CreatePackageAsync(
            packageId, maxAccessCount, isPrivate, guestId, userId, timeToLiveHours, unlockVerifier);

        var createdContentId = contentId ?? Guid.NewGuid();
        var response = await SendUploadItemAsync(
            createdPackageId, kind, bytes ?? SmallItemBytes, meta, createdContentId, guestId, userId);
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);

        return (createdPackageId, createdContentId);
    }

    /// <summary>
    /// Sends a multipart POST of one item to an existing package. The parts are
    /// ordered as the endpoint requires: metadata first, then the payload.
    /// </summary>
    /// <param name="declaredSize">
    /// Value of the <c>Size</c> part. Defaults to the actual payload length;
    /// pass a different value to exercise size validation.
    /// </param>
    protected async Task<HttpResponseMessage> SendUploadItemAsync(
        Guid packageId,
        string kind,
        byte[] bytes,
        string meta = "bWV0YQ",
        Guid? contentId = null,
        Guid? guestId = null,
        Guid? userId = null,
        string fileName = "item.bin",
        long? declaredSize = null)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent((contentId ?? Guid.NewGuid()).ToString()), "Id" },
            { new StringContent(kind), "Kind" },
            { new StringContent(meta), "Meta" },
            {
                new StringContent((declaredSize ?? bytes.Length).ToString(CultureInfo.InvariantCulture)),
                "Size"
            }
        };

        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "File", fileName);

        return await SendRawUploadAsync(packageId, form, guestId, userId);
    }

    /// <summary>
    /// Sends an arbitrary multipart body to the upload endpoint so tests can
    /// exercise part order, missing parts and extra parts.
    /// </summary>
    protected async Task<HttpResponseMessage> SendRawUploadAsync(
        Guid packageId,
        HttpContent content,
        Guid? guestId = null,
        Guid? userId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/content/package/{packageId}/upload")
        {
            Content = content
        };
        AddIdentity(request, guestId, userId);

        var response = await Client.SendAsync(request);
        // The endpoint may stop reading after the metadata parts, in which case
        // the request content must not be reused or disposed twice.
        return response;
    }

    /// <summary>
    /// Sends a multipart POST with no content part to exercise validation.
    /// </summary>
    protected async Task<HttpResponseMessage> SendUploadWithoutFileAsync(
        Guid packageId,
        Guid? guestId = null,
        Guid? userId = null)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(Guid.NewGuid().ToString()), "Id" },
            { new StringContent("file"), "Kind" },
            { new StringContent("bWV0YQ"), "Meta" },
            { new StringContent("1"), "Size" }
        };

        return await SendRawUploadAsync(packageId, form, guestId, userId);
    }

    protected Task<HttpResponseMessage> GetPackageAsync(
        Guid packageId,
        Guid? guestId = null,
        Guid? userId = null,
        string? unlockProof = null,
        string? accessSession = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/content/package/{packageId}");
        AddIdentity(request, guestId, userId);
        if (unlockProof is not null)
        {
            request.Headers.Add("X-Unlock-Proof", unlockProof);
        }
        if (accessSession is not null)
        {
            request.Headers.Add("X-Access-Session", accessSession);
        }

        return Client.SendAsync(request);
    }

    /// <summary>
    /// Fetches a package and asserts the request succeeded.
    /// </summary>
    protected async Task<ContentPackageDetailsResponse> GetPackageDetailsAsync(
        Guid packageId,
        Guid? guestId = null,
        Guid? userId = null,
        string? unlockProof = null,
        string? accessSession = null)
    {
        var response = await GetPackageAsync(packageId, guestId, userId, unlockProof, accessSession);
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);

        return await ReadJsonAsync<ContentPackageDetailsResponse>(response);
    }

    /// <summary>
    /// Sends GET /packages with an optional page size and cursor.
    /// </summary>
    protected Task<HttpResponseMessage> ListPackagesAsync(
        Guid? guestId = null,
        Guid? userId = null,
        int? take = null,
        string? cursor = null)
    {
        var query = new List<string>();
        if (take.HasValue)
        {
            query.Add($"take={take.Value}");
        }

        if (!string.IsNullOrEmpty(cursor))
        {
            query.Add($"cursor={Uri.EscapeDataString(cursor)}");
        }

        var url = "/api/v1/content/packages" + (query.Count > 0 ? "?" + string.Join("&", query) : string.Empty);
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddIdentity(request, guestId, userId);
        return Client.SendAsync(request);
    }

    protected static string DownloadUrl(Guid packageId, Guid contentId)
        => $"/api/v1/content/package/{packageId}/content/{contentId}/download";

    /// <summary>
    /// Fetches a content item with the package's access session.
    /// </summary>
    protected Task<HttpResponseMessage> SendDownloadAsync(
        string downloadUrl,
        string? accessSession,
        Guid? guestId,
        Guid? userId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        if (accessSession is not null)
        {
            request.Headers.Add("X-Access-Session", accessSession);
        }
        if (guestId.HasValue)
        {
            request.Headers.Add("X-Guest-ID", guestId.Value.ToString());
        }
        if (userId.HasValue)
        {
            request.Headers.Add(TestAuthHandler.UserIdHeader, userId.Value.ToString());
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
    /// Invokes the hosted cleanup service's private run method directly. The
    /// host starts the service with a long interval, so the next timer tick
    /// would never arrive during a test.
    /// </summary>
    protected Task RunCleanupAsync()
    {
        var cleanup = Services.GetServices<IHostedService>().OfType<CleanupService>().Single();
        var method = typeof(CleanupService)
            .GetMethod("RunCleanupAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        return (Task)method.Invoke(cleanup, [CancellationToken.None])!;
    }

    /// <summary>
    /// Counts all objects in the test's RustFS bucket.
    /// </summary>
    protected async Task<int> CountStoredObjectsAsync()
    {
        try
        {
            var response = await S3Client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = BucketName });
            return response.S3Objects?.Count ?? 0;
        }
        catch (NoSuchBucketException)
        {
            // No upload ever reached S3, so the bucket was never created.
            return 0;
        }
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

    /// <summary>
    /// Base64url-encoded opaque metadata blob used by upload requests.
    /// </summary>
    protected static string MetaFor(string? name, string mime)
        => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { name, mime })));

    /// <summary>
    /// Base64url verifier of 32 bytes, e.g. for protected packages.
    /// </summary>
    protected static string Verifier(byte fill = 1)
        => Base64Url.EncodeToString(Enumerable.Repeat(fill, 32).ToArray());

    protected static byte[] DecodePayload(string payload) => Base64Url.DecodeFromChars(payload);

    protected sealed record ProblemBody(int Status, string Title, string? Detail);
}
