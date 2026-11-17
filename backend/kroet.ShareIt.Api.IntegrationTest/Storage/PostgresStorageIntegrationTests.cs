using kroet.ShareIt.Api.Configuration;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;
using kroet.ShareIt.Api.Models;
using kroet.ShareIt.Api.Storage;
using kroet.ShareIt.Api.Storage.Postgres;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ContentModel = kroet.ShareIt.Api.Models.Content;

namespace kroet.ShareIt.Api.IntegrationTest.Storage;

[TestClass]
public class PostgresStorageIntegrationTests
{
    private static PostgresIntegrationDatabase _database = null!;

    private SqlContentStorageHandler _contentStorageHandler = null!;
    private IMetricsSnapshotProvider _metricsSnapshotProvider = null!;
    private MutableTimeProvider _timeProvider = null!;
    private DownloadSettings _downloadSettings = null!;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext _)
    {
        _database = await PostgresIntegrationDatabase.StartAsync();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    [TestInitialize]
    public async Task TestInitialize()
    {
        await _database.ResetAsync();

        var sqlSettings = Options.Create(new SqlStorageSettings
        {
            ConnectionString = _database.ConnectionString,
            AutoInitializeSchema = false
        });
        var downloadSettings = Options.Create(new DownloadSettings
        {
            AccessSessionLifetimeMinutes = 15,
            ExhaustedPackageRetentionMinutes = 30
        });
        _downloadSettings = downloadSettings.Value;

        _timeProvider = new MutableTimeProvider(DateTimeOffset.UtcNow);
        _contentStorageHandler = new SqlContentStorageHandler(
            sqlSettings,
            NullLogger<SqlContentStorageHandler>.Instance,
            _timeProvider,
            downloadSettings);
        _metricsSnapshotProvider = _contentStorageHandler;
    }

    [TestMethod]
    public async Task StorePackageAsync_AndGetPackageAsync_RoundTripsPackageWithContent()
    {
        var lastAccessAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var package = CreatePackage(maxAccessCount: 3, isPrivate: true) with
        {
            LastAccessAt = lastAccessAt
        };
        var content = new ExternalContent(CreateContent(package.Id), "https://blob.example/file-1");

        await _contentStorageHandler.StorePackageAsync(package);
        await _contentStorageHandler.StoreContentAsync(content);

        var storedPackage = await _contentStorageHandler.GetPackageAsync(package.Id, includeContent: true);

        Assert.IsNotNull(storedPackage);
        Assert.AreEqual(package.Id, storedPackage.Id);
        Assert.AreEqual(package.UnlockVerifier, storedPackage.UnlockVerifier);
        Assert.AreEqual(package.MaxAccessCount, storedPackage.MaxAccessCount);
        Assert.AreEqual(package.IsPrivate, storedPackage.IsPrivate);
        Assert.IsNotNull(storedPackage.LastAccessAt);
        Assert.IsTrue(
            Math.Abs((storedPackage.LastAccessAt.Value - lastAccessAt).TotalMilliseconds) < 1,
            "PostgreSQL stores timestamps at microsecond precision.");
        Assert.IsNotNull(storedPackage.Contents);
        Assert.HasCount(1, storedPackage.Contents);
        Assert.AreEqual(content.Id, storedPackage.Contents[0].Id);
        Assert.AreEqual(content.Url, storedPackage.Contents[0].Url);
    }

    [TestMethod]
    public async Task CreateAccessSessionAsync_ReportsWhenLimitIsReached()
    {
        var package = CreatePackage(maxAccessCount: 2);
        await _contentStorageHandler.StorePackageAsync(package);

        var firstIncrement = await CreateAccessSessionAsync(package.Id);
        var secondIncrement = await CreateAccessSessionAsync(package.Id);
        var rejected = await CreateAccessSessionAsync(package.Id);

        Assert.IsTrue(firstIncrement.Success);
        Assert.AreEqual(1, firstIncrement.NewAccessCount);
        Assert.IsTrue(secondIncrement.Success);
        Assert.AreEqual(2, secondIncrement.NewAccessCount);
        Assert.IsFalse(rejected.Success);
    }

    [TestMethod]
    public async Task DeletePackageAsync_RemovesPackageAndItsContents()
    {
        var package = CreatePackage();
        var content = new ExternalContent(CreateContent(package.Id), "https://blob.example/file-1");

        await _contentStorageHandler.StorePackageAsync(package);
        await _contentStorageHandler.StoreContentAsync(content);

        var deleted = await _contentStorageHandler.DeletePackageAsync(package.Id);
        var storedPackage = await _contentStorageHandler.GetPackageAsync(package.Id, includeContent: true);
        var storedContent = await _contentStorageHandler.GetContentAsync(content.Id);

        Assert.IsTrue(deleted);
        Assert.IsNull(storedPackage);
        Assert.IsNull(storedContent);
    }

    [TestMethod]
    public async Task CreateAccessSessionAsync_RejectsExpiredPackage()
    {
        var package = CreatePackage(expiresAt: _timeProvider.GetUtcNow().AddMinutes(-1));
        await _contentStorageHandler.StorePackageAsync(package);

        var claim = await CreateAccessSessionAsync(package.Id);

        Assert.IsFalse(claim.Success);
        var stored = await _contentStorageHandler.GetPackageShellAsync(package.Id);
        Assert.IsNotNull(stored);
        Assert.AreEqual(0, stored.CurrentAccessCount);
    }

    [TestMethod]
    public async Task CreateAccessSessionAsync_RoundTripsTheSessionAndCascadesOnPackageDelete()
    {
        var package = CreatePackage(maxAccessCount: 2);
        await _contentStorageHandler.StorePackageAsync(package);

        var session = NewSession(package.Id);
        var result = await _contentStorageHandler.CreateAccessSessionAsync(session);
        var stored = await _contentStorageHandler.GetAccessSessionByTokenHashAsync(session.TokenHash);

        Assert.IsTrue(result.Success);
        Assert.IsNotNull(stored);
        Assert.AreEqual(package.Id, stored.PackageId);
        Assert.AreEqual(session.ClientId, stored.ClientId);
        Assert.AreEqual(session.IsGuest, stored.IsGuest);

        Assert.IsTrue(await _contentStorageHandler.DeletePackageAsync(package.Id));
        Assert.IsNull(await _contentStorageHandler.GetAccessSessionByTokenHashAsync(session.TokenHash));
    }

    [TestMethod]
    public async Task StoreContentsAsync_GetContentsAsync_AndGetPackagesAsync_RoundTripBatch()
    {
        var firstPackage = CreatePackage();
        var secondPackage = CreatePackage();
        var firstContent = new ExternalContent(CreateContent(firstPackage.Id), "https://blob.example/file-1");
        var secondContent = new ExternalContent(CreateContent(secondPackage.Id), "https://blob.example/file-2");

        await _contentStorageHandler.StorePackageAsync(firstPackage);
        await _contentStorageHandler.StorePackageAsync(secondPackage);
        await _contentStorageHandler.StoreContentsAsync([firstContent, secondContent]);
        await _contentStorageHandler.StoreContentsAsync([]);

        var contents = await _contentStorageHandler.GetContentsAsync([firstContent.Id, secondContent.Id]);
        var packagesWithContent = await _contentStorageHandler.GetPackagesAsync(
            [firstPackage.Id, secondPackage.Id],
            includeContent: true);
        var packagesWithoutContent = await _contentStorageHandler.GetPackagesAsync(
            [firstPackage.Id, secondPackage.Id],
            includeContent: false);
        var emptyContents = await _contentStorageHandler.GetContentsAsync([]);
        var emptyPackages = await _contentStorageHandler.GetPackagesAsync([]);

        Assert.HasCount(2, contents);
        Assert.HasCount(2, packagesWithContent);
        Assert.HasCount(1, packagesWithContent.Single(package => package.Id == firstPackage.Id).Contents!);
        Assert.HasCount(1, packagesWithContent.Single(package => package.Id == secondPackage.Id).Contents!);
        Assert.HasCount(2, packagesWithoutContent);
        Assert.IsTrue(packagesWithoutContent.All(package => package.Contents is null));
        Assert.IsEmpty(emptyContents);
        Assert.IsEmpty(emptyPackages);
    }

    [TestMethod]
    public async Task ExistsPackageAsync_AndExistsContentAsync_ReflectStoredRows()
    {
        var package = CreatePackage();
        var content = new ExternalContent(CreateContent(package.Id), "https://blob.example/file-1");

        await _contentStorageHandler.StorePackageAsync(package);
        await _contentStorageHandler.StoreContentAsync(content);

        Assert.IsTrue(await _contentStorageHandler.ExistsPackageAsync(package.Id));
        Assert.IsFalse(await _contentStorageHandler.ExistsPackageAsync(Guid.NewGuid()));
        Assert.IsTrue(await _contentStorageHandler.ExistsContentAsync(content.Id));
        Assert.IsFalse(await _contentStorageHandler.ExistsContentAsync(Guid.NewGuid()));
    }

    [TestMethod]
    public async Task DeletePackagesAsync_AndDeleteContentsAsync_DeleteOnlyRequestedRows()
    {
        var firstPackage = CreatePackage();
        var secondPackage = CreatePackage();
        var firstContent = new ExternalContent(CreateContent(firstPackage.Id), "https://blob.example/file-1");
        var secondContent = new ExternalContent(CreateContent(secondPackage.Id), "https://blob.example/file-2");

        await _contentStorageHandler.StorePackageAsync(firstPackage);
        await _contentStorageHandler.StorePackageAsync(secondPackage);
        await _contentStorageHandler.StoreContentsAsync([firstContent, secondContent]);

        Assert.AreEqual(1, await _contentStorageHandler.DeleteContentsAsync([firstContent.Id]));
        Assert.AreEqual(0, await _contentStorageHandler.DeleteContentsAsync([]));
        Assert.IsFalse(await _contentStorageHandler.ExistsContentAsync(firstContent.Id));
        Assert.IsTrue(await _contentStorageHandler.ExistsContentAsync(secondContent.Id));

        Assert.AreEqual(1, await _contentStorageHandler.DeletePackagesAsync([firstPackage.Id]));
        Assert.AreEqual(0, await _contentStorageHandler.DeletePackagesAsync([]));
        Assert.IsFalse(await _contentStorageHandler.ExistsPackageAsync(firstPackage.Id));
        Assert.IsTrue(await _contentStorageHandler.ExistsPackageAsync(secondPackage.Id));
    }

    [TestMethod]
    public async Task GetPackagesToDeleteAsync_ReturnsExpiredImmediatelyAndExhaustedAfterRetention()
    {
        var activePackage = CreatePackage();
        var expiredPackage = CreatePackage(expiresAt: DateTimeOffset.UtcNow.AddHours(-1));
        var limitedPackage = CreatePackage(maxAccessCount: 1);

        await _contentStorageHandler.StorePackageAsync(activePackage);
        await _contentStorageHandler.StorePackageAsync(expiredPackage);
        await _contentStorageHandler.StorePackageAsync(limitedPackage);
        await CreateAccessSessionAsync(limitedPackage.Id);

        // The exhausted package keeps serving its outstanding downloads for
        // the retention period and must not be cleanup-eligible yet.
        var retained = await _contentStorageHandler.GetPackagesToDeleteAsync();
        CollectionAssert.AreEquivalent(new[] { expiredPackage.Id }, retained.ToArray());
        Assert.DoesNotContain(limitedPackage.Id, retained);

        _timeProvider.UtcNow = _timeProvider.UtcNow.AddMinutes(_downloadSettings.ExhaustedPackageRetentionMinutes + 1);

        var eligible = await _contentStorageHandler.GetPackagesToDeleteAsync();
        CollectionAssert.AreEquivalent(
            new[] { expiredPackage.Id, limitedPackage.Id },
            eligible.ToArray());
        Assert.DoesNotContain(activePackage.Id, eligible);
    }

    [TestMethod]
    public async Task DeletePackageAsync_QueuesContentBlobsTransactionally()
    {
        var package = CreatePackage();
        var content = new ExternalContent(CreateContent(package.Id), "https://blob.example/file-1");

        await _contentStorageHandler.StorePackageAsync(package);
        await _contentStorageHandler.StoreContentAsync(content);

        var deleted = await _contentStorageHandler.DeletePackageAsync(package.Id);

        Assert.IsTrue(deleted);
        Assert.IsNull(await _contentStorageHandler.GetPackageAsync(package.Id, false));
        Assert.IsNull(await _contentStorageHandler.GetContentAsync(content.Id));

        var pending = await _contentStorageHandler.GetPendingBlobDeletionsAsync(10);
        CollectionAssert.AreEqual(new[] { content.Url }, pending.ToArray());

        await _contentStorageHandler.CompleteBlobDeletionAsync(content.Url);
        Assert.IsEmpty(await _contentStorageHandler.GetPendingBlobDeletionsAsync(10));
    }

    [TestMethod]
    public async Task DeleteContentAsync_QueuesItsBlob()
    {
        var package = CreatePackage();
        var content = new ExternalContent(CreateContent(package.Id), "https://blob.example/file-1");

        await _contentStorageHandler.StorePackageAsync(package);
        await _contentStorageHandler.StoreContentAsync(content);

        Assert.IsTrue(await _contentStorageHandler.DeleteContentAsync(content.Id));

        var pending = await _contentStorageHandler.GetPendingBlobDeletionsAsync(10);
        CollectionAssert.AreEqual(new[] { content.Url }, pending.ToArray());
    }

    [TestMethod]
    public async Task DeletePackagesAsync_DeduplicatesSharedBlobKeys()
    {
        var firstPackage = CreatePackage();
        var secondPackage = CreatePackage();
        var sharedUrl = "https://blob.example/shared";

        await _contentStorageHandler.StorePackageAsync(firstPackage);
        await _contentStorageHandler.StorePackageAsync(secondPackage);
        await _contentStorageHandler.StoreContentsAsync(
        [
            new ExternalContent(CreateContent(firstPackage.Id), sharedUrl),
            new ExternalContent(CreateContent(secondPackage.Id), sharedUrl)
        ]);

        Assert.AreEqual(2, await _contentStorageHandler.DeletePackagesAsync([firstPackage.Id, secondPackage.Id]));

        var pending = await _contentStorageHandler.GetPendingBlobDeletionsAsync(10);
        Assert.HasCount(1, pending);
        Assert.AreEqual(sharedUrl, pending[0]);
    }

    [TestMethod]
    public async Task GetMetricsSnapshotAsync_ReportsPackageAndContentAggregates()
    {
        var now = _timeProvider.GetUtcNow();
        var activePackage = CreatePackage(expiresAt: now.AddHours(1), isPrivate: true);
        var expiredPackage = CreatePackage(expiresAt: now.AddHours(-1)) with { UnlockVerifier = null };
        var exhaustedPackage = CreatePackage(maxAccessCount: 1) with { UnlockVerifier = null };

        await _contentStorageHandler.StorePackageAsync(activePackage);
        await _contentStorageHandler.StorePackageAsync(expiredPackage);
        await _contentStorageHandler.StorePackageAsync(exhaustedPackage);
        await _contentStorageHandler.StoreContentAsync(
            new ExternalContent(CreateContent(activePackage.Id), "https://blob.example/file-1"));
        await CreateAccessSessionAsync(exhaustedPackage.Id);

        var snapshot = await _metricsSnapshotProvider.GetMetricsSnapshotAsync(now);

        Assert.AreEqual(3, snapshot.TotalPackages);
        Assert.AreEqual(2, snapshot.ActivePackages);
        Assert.AreEqual(1, snapshot.ExpiredPackages);
        Assert.AreEqual(1, snapshot.ExhaustedPackages);
        Assert.AreEqual(1, snapshot.ProtectedPackages);
        Assert.AreEqual(1, snapshot.PrivatePackages);
        Assert.AreEqual(2, snapshot.EmptyPackages);
        Assert.AreEqual(1, snapshot.TotalItems);
        Assert.AreEqual(128, snapshot.TotalBytes);
        Assert.AreEqual(1, snapshot.CleanupPending);
    }

    [TestMethod]
    public async Task ListPackagesAsync_ReturnsOwnPackagesNewestFirstWithAggregatesAndCursor()
    {
        var ownerId = Guid.NewGuid();
        var start = DateTimeOffset.UtcNow;

        var oldest = CreatePackage(createdAt: start, userId: ownerId);
        var middle = CreatePackage(createdAt: start.AddMinutes(1), userId: ownerId);
        var newest = CreatePackage(createdAt: start.AddMinutes(2), userId: ownerId);
        var foreign = CreatePackage(createdAt: start.AddMinutes(3), userId: Guid.NewGuid());

        foreach (var package in new[] { oldest, middle, newest, foreign })
        {
            await _contentStorageHandler.StorePackageAsync(package);
        }

        await _contentStorageHandler.StoreContentAsync(
            new ExternalContent(CreateContent(oldest.Id) with { Size = 10 }, "blob://a"));
        await _contentStorageHandler.StoreContentAsync(
            new ExternalContent(CreateContent(oldest.Id) with { Size = 20 }, "blob://b"));

        var firstPage = await _contentStorageHandler.ListPackagesAsync(ownerId, cursor: null, take: 2);

        Assert.HasCount(2, firstPage.Items);
        Assert.AreEqual(newest.Id, firstPage.Items[0].Id);
        Assert.AreEqual(middle.Id, firstPage.Items[1].Id);
        Assert.IsNotNull(firstPage.NextCursor);

        var secondPage = await _contentStorageHandler.ListPackagesAsync(ownerId, firstPage.NextCursor, take: 2);

        Assert.HasCount(1, secondPage.Items);
        Assert.AreEqual(oldest.Id, secondPage.Items[0].Id);
        Assert.AreEqual(2, secondPage.Items[0].ItemCount);
        Assert.AreEqual(30, secondPage.Items[0].TotalSize);
        Assert.IsNull(secondPage.NextCursor);
    }

    [TestMethod]
    public async Task GetAccessSessionByTokenHashAsync_ReturnsNullForUnknownHash()
    {
        Assert.IsNull(await _contentStorageHandler.GetAccessSessionByTokenHashAsync("unknown-hash"));
    }

    [TestMethod]
    public async Task DeleteExpiredAccessSessionsAsync_RemovesOnlyExpiredSessions()
    {
        var package = CreatePackage(maxAccessCount: 3);
        await _contentStorageHandler.StorePackageAsync(package);

        var first = NewSession(package.Id, expiresAt: _timeProvider.UtcNow.AddMinutes(15));
        var second = NewSession(package.Id, expiresAt: _timeProvider.UtcNow.AddMinutes(30));
        await _contentStorageHandler.CreateAccessSessionAsync(first);
        await _contentStorageHandler.CreateAccessSessionAsync(second);

        _timeProvider.UtcNow = _timeProvider.UtcNow.AddMinutes(16);
        var deleted = await _contentStorageHandler.DeleteExpiredAccessSessionsAsync(_timeProvider.UtcNow);

        Assert.AreEqual(1, deleted);
        Assert.IsNull(await _contentStorageHandler.GetAccessSessionByTokenHashAsync(first.TokenHash));
        Assert.IsNotNull(await _contentStorageHandler.GetAccessSessionByTokenHashAsync(second.TokenHash));
    }

    private Task<AccessSessionClaimResult> CreateAccessSessionAsync(Guid packageId)
        => _contentStorageHandler.CreateAccessSessionAsync(NewSession(packageId));

    private AccessSession NewSession(
        Guid packageId,
        Guid? clientId = null,
        bool isGuest = false,
        DateTimeOffset? expiresAt = null)
        => new()
        {
            Id = Guid.NewGuid(),
            PackageId = packageId,
            ClientId = clientId ?? Guid.NewGuid(),
            IsGuest = isGuest,
            TokenHash = Guid.NewGuid().ToString("N"),
            CreatedAt = _timeProvider.GetUtcNow(),
            ExpiresAt = expiresAt ?? _timeProvider.GetUtcNow().AddMinutes(_downloadSettings.AccessSessionLifetimeMinutes)
        };

    private static ContentPackage CreatePackage(
        int? maxAccessCount = null,
        bool? isPrivate = null,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? createdAt = null,
        Guid? userId = null)
    {
        return new ContentPackage
        {
            Id = Guid.NewGuid(),
            UnlockVerifier = "verifier",
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            UserId = userId ?? Guid.NewGuid(),
            IsPrivate = isPrivate,
            MaxAccessCount = maxAccessCount,
            ExpiresAt = expiresAt
        };
    }

    private static ContentModel CreateContent(Guid packageId)
    {
        return new ContentModel
        {
            Id = Guid.NewGuid(),
            PackageId = packageId,
            ContentType = ContentType.File,
            CreatedAt = DateTimeOffset.UtcNow,
            Size = 128,
            UserId = Guid.NewGuid(),
            Meta = "bWV0YQ"
        };
    }
}
