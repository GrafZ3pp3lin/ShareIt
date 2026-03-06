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

        _contentStorageHandler = new SqlContentStorageHandler(sqlSettings, NullLogger<SqlContentStorageHandler>.Instance);
        _downloadTokenService = new SqlDownloadTokenService(
            sqlSettings,
            downloadSettings,
            TimeProvider.System,
            NullLogger<SqlDownloadTokenService>.Instance);
    }

    [TestMethod]
    public async Task StorePackageAsync_AndGetPackageAsync_RoundTripsPackageWithContent()
    {
        var package = CreatePackage(maxDownloadCount: 3);
        var content = new ExternalContent(CreateContent(package.Id), "https://blob.example/file-1");

        await _contentStorageHandler.StorePackageAsync(package);
        await _contentStorageHandler.StoreContentAsync(content);

        var storedPackage = await _contentStorageHandler.GetPackageAsync(package.Id, includeContent: true);

        Assert.IsNotNull(storedPackage);
        Assert.AreEqual(package.Id, storedPackage.Id);
        Assert.AreEqual(package.Title, storedPackage.Title);
        Assert.AreEqual(package.MaxDownloadCount, storedPackage.MaxDownloadCount);
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
    public async Task GenerateTokenAsync_ValidateAndConsumeTokenAsync_AllowsSingleUse()
    {
        var contentId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        var token = await _downloadTokenService.GenerateTokenAsync(contentId, clientId);
        var firstValidation = await _downloadTokenService.ValidateAndConsumeTokenAsync(token.Token, clientId);
        var secondValidation = await _downloadTokenService.ValidateAndConsumeTokenAsync(token.Token, clientId);

        Assert.IsTrue(firstValidation.IsValid);
        Assert.AreEqual(contentId, firstValidation.ContentId);
        Assert.IsFalse(secondValidation.IsValid);
        Assert.AreEqual("Token has already been used", secondValidation.ErrorMessage);
    }

    [TestMethod]
    public async Task ValidateAndConsumeTokenAsync_ReturnsInvalidForWrongClient()
    {
        var contentId = Guid.NewGuid();
        var validClientId = Guid.NewGuid();
        var invalidClientId = Guid.NewGuid();

        var token = await _downloadTokenService.GenerateTokenAsync(contentId, validClientId);
        var validation = await _downloadTokenService.ValidateAndConsumeTokenAsync(token.Token, invalidClientId);

        Assert.IsFalse(validation.IsValid);
        Assert.AreEqual("Token is not valid for this client", validation.ErrorMessage);
    }

    private static ContentPackage CreatePackage(int? maxDownloadCount = null)
    {
        return new ContentPackage
        {
            Id = Guid.NewGuid(),
            Title = "Integration test package",
            Description = "Round-trip through PostgreSQL",
            CreatedAt = DateTimeOffset.UtcNow,
            UserId = Guid.NewGuid(),
            MaxDownloadCount = maxDownloadCount
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
            MimeType = "text/plain",
            FileName = "integration.txt"
        };
    }
}
