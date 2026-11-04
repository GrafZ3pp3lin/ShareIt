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
    private SqlDownloadTokenService _downloadTokenService = null!;
    private MutableTimeProvider _timeProvider = null!;

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
            DefaultTokenExpirationMinutes = 15,
            UsedTokenRetentionHours = 24
        });

        _timeProvider = new MutableTimeProvider(DateTimeOffset.UtcNow);
        _contentStorageHandler = new SqlContentStorageHandler(sqlSettings, NullLogger<SqlContentStorageHandler>.Instance);
        _downloadTokenService = new SqlDownloadTokenService(
            sqlSettings,
            downloadSettings,
            _timeProvider,
            NullLogger<SqlDownloadTokenService>.Instance);
    }

    [TestMethod]
    public async Task StorePackageAsync_AndGetPackageAsync_RoundTripsPackageWithContent()
    {
        var package = CreatePackage(maxDownloadCount: 3, isPrivate: true);
        var content = new ExternalContent(CreateContent(package.Id), "https://blob.example/file-1");

        await _contentStorageHandler.StorePackageAsync(package);
        await _contentStorageHandler.StoreContentAsync(content);

        var storedPackage = await _contentStorageHandler.GetPackageAsync(package.Id, includeContent: true);

        Assert.IsNotNull(storedPackage);
        Assert.AreEqual(package.Id, storedPackage.Id);
        Assert.AreEqual(package.UnlockVerifier, storedPackage.UnlockVerifier);
        Assert.AreEqual(package.MaxDownloadCount, storedPackage.MaxDownloadCount);
        Assert.AreEqual(package.IsPrivate, storedPackage.IsPrivate);
        Assert.IsNotNull(storedPackage.Contents);
        Assert.HasCount(1, storedPackage.Contents);
        Assert.AreEqual(content.Id, storedPackage.Contents[0].Id);
        Assert.AreEqual(content.Url, storedPackage.Contents[0].Url);
    }

    [TestMethod]
    public async Task IncrementPackageAccessCountAsync_ReportsWhenLimitIsReached()
    {
        var package = CreatePackage(maxDownloadCount: 2);
        await _contentStorageHandler.StorePackageAsync(package);

        var firstIncrement = await _contentStorageHandler.IncrementPackageAccessCountAsync(package.Id);
        var secondIncrement = await _contentStorageHandler.IncrementPackageAccessCountAsync(package.Id);

        Assert.IsTrue(firstIncrement.Success);
        Assert.AreEqual(1, firstIncrement.NewCount);
        Assert.IsFalse(firstIncrement.LimitReached);
        Assert.IsTrue(secondIncrement.Success);
        Assert.AreEqual(2, secondIncrement.NewCount);
        Assert.IsTrue(secondIncrement.LimitReached);
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
    public async Task ValidateTokenAsync_DoesNotConsume_AndConsumeTokenAsync_AllowsSingleUse()
    {
        var contentId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        var token = await _downloadTokenService.GenerateTokenAsync(contentId, clientId);
        var firstValidation = await _downloadTokenService.ValidateTokenAsync(token.Token, clientId);
        var secondValidation = await _downloadTokenService.ValidateTokenAsync(token.Token, clientId);
        var consumed = await _downloadTokenService.ConsumeTokenAsync(token.Token, clientId);
        var reused = await _downloadTokenService.ConsumeTokenAsync(token.Token, clientId);

        Assert.IsTrue(firstValidation.IsValid);
        Assert.IsTrue(secondValidation.IsValid);
        Assert.AreEqual(contentId, firstValidation.ContentId);
        Assert.IsTrue(consumed.IsValid);
        Assert.IsFalse(reused.IsValid);
        Assert.AreEqual("Token has already been used", reused.ErrorMessage);
    }

    [TestMethod]
    public async Task ValidateTokenAsync_ReturnsInvalidForWrongClient()
    {
        var contentId = Guid.NewGuid();
        var validClientId = Guid.NewGuid();
        var invalidClientId = Guid.NewGuid();

        var token = await _downloadTokenService.GenerateTokenAsync(contentId, validClientId);
        var validation = await _downloadTokenService.ValidateTokenAsync(token.Token, invalidClientId);

        Assert.IsFalse(validation.IsValid);
        Assert.AreEqual("Token is not valid for this client", validation.ErrorMessage);
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
    public async Task GetPackagesToDeleteAsync_ReturnsExpiredAndLimitReachedPackages()
    {
        var activePackage = CreatePackage();
        var expiredPackage = CreatePackage(expiresAt: DateTimeOffset.UtcNow.AddHours(-1));
        var limitedPackage = CreatePackage(maxDownloadCount: 1);

        await _contentStorageHandler.StorePackageAsync(activePackage);
        await _contentStorageHandler.StorePackageAsync(expiredPackage);
        await _contentStorageHandler.StorePackageAsync(limitedPackage);
        await _contentStorageHandler.IncrementPackageAccessCountAsync(limitedPackage.Id);

        var packagesToDelete = await _contentStorageHandler.GetPackagesToDeleteAsync();

        CollectionAssert.AreEquivalent(
            new[] { expiredPackage.Id, limitedPackage.Id },
            packagesToDelete.ToArray());
        Assert.DoesNotContain(activePackage.Id, packagesToDelete);
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
    public async Task ValidateTokenAsync_ReturnsInvalidForUnknownToken()
    {
        var result = await _downloadTokenService.ValidateTokenAsync("unknown-token", Guid.NewGuid());

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("Invalid token", result.ErrorMessage);
    }

    [TestMethod]
    public async Task ValidateTokenAsync_ReturnsInvalidForExpiredToken()
    {
        var token = await _downloadTokenService.GenerateTokenAsync(Guid.NewGuid(), Guid.NewGuid());

        _timeProvider.UtcNow = token.ExpiresAt.AddSeconds(1);

        var result = await _downloadTokenService.ValidateTokenAsync(token.Token, token.ClientId);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("Token has expired", result.ErrorMessage);
    }

    [TestMethod]
    public async Task CleanupExpiredTokensAsync_RemovesOnlyExpiredTokens()
    {
        var clientId = Guid.NewGuid();
        var secondContentId = Guid.NewGuid();
        var firstToken = await _downloadTokenService.GenerateTokenAsync(Guid.NewGuid(), clientId);

        _timeProvider.UtcNow = _timeProvider.UtcNow.AddMinutes(10);
        var secondToken = await _downloadTokenService.GenerateTokenAsync(secondContentId, clientId);

        _timeProvider.UtcNow = _timeProvider.UtcNow.AddMinutes(6);
        var deleted = await _downloadTokenService.CleanupExpiredTokensAsync();

        Assert.AreEqual(1, deleted);
        Assert.IsFalse((await _downloadTokenService.ValidateTokenAsync(firstToken.Token, clientId)).IsValid);

        var stillValid = await _downloadTokenService.ConsumeTokenAsync(secondToken.Token, clientId);
        Assert.IsTrue(stillValid.IsValid);
        Assert.AreEqual(secondContentId, stillValid.ContentId);
    }

    private static ContentPackage CreatePackage(
        int? maxDownloadCount = null,
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
            MaxDownloadCount = maxDownloadCount,
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
