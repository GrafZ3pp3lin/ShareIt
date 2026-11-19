using System.Globalization;
using kroet.ShareIt.Api.Configuration;
using kroet.ShareIt.Api.Models;
using kroet.ShareIt.Api.Storage.InMemory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using ContentModel = kroet.ShareIt.Api.Models.Content;

namespace kroet.ShareIt.Api.Test.Storage.InMemory;

[TestClass]
public class InMemoryContentStorageHandlerTests
{
    private readonly InMemoryContentStorageHandler _handler = new();

    [TestMethod]
    public async Task StorePackageAsync_AndGetPackageAsync_SupportsWithAndWithoutContent()
    {
        var package = CreatePackage();
        var firstContent = CreateContent(package.Id);
        var secondContent = CreateContent(package.Id);

        await _handler.StorePackageAsync(package);
        await _handler.StoreContentAsync(firstContent);
        await _handler.StoreContentAsync(secondContent);

        var withContent = await _handler.GetPackageAsync(package.Id, includeContent: true);
        var withoutContent = await _handler.GetPackageAsync(package.Id, includeContent: false);
        var missing = await _handler.GetPackageAsync(Guid.NewGuid());

        Assert.IsNotNull(withContent);
        Assert.HasCount(2, withContent.Contents!);
        Assert.IsNotNull(withoutContent);
        Assert.IsNull(withoutContent.Contents);
        Assert.IsNull(missing);
    }

    [TestMethod]
    public async Task GetContentAsync_ReturnsMatchingItems()
    {
        var firstContent = CreateContent(Guid.NewGuid());
        var secondContent = CreateContent(Guid.NewGuid());

        await _handler.StoreContentAsync(firstContent);
        await _handler.StoreContentAsync(secondContent);

        var found = await _handler.GetContentAsync(firstContent.Id);
        var missing = await _handler.GetContentAsync(Guid.NewGuid());

        Assert.AreSame(firstContent, found);
        Assert.IsNull(missing);
    }

    [TestMethod]
    public async Task CreateAccessSessionAsync_HandlesMissingLimitReachedUnlimitedAndSuccess()
    {
        var missing = await _handler.CreateAccessSessionAsync(NewSession(Guid.NewGuid()));

        var limitedPackage = CreatePackage(maxAccessCount: 2, currentAccessCount: 2);
        await _handler.StorePackageAsync(limitedPackage);
        var alreadyLimited = await _handler.CreateAccessSessionAsync(NewSession(limitedPackage.Id));

        var successfulPackage = CreatePackage(maxAccessCount: 2, currentAccessCount: 0);
        await _handler.StorePackageAsync(successfulPackage);
        var firstIncrement = await _handler.CreateAccessSessionAsync(NewSession(successfulPackage.Id));
        var secondIncrement = await _handler.CreateAccessSessionAsync(NewSession(successfulPackage.Id));

        var unlimitedPackage = CreatePackage(maxAccessCount: null, currentAccessCount: 5);
        await _handler.StorePackageAsync(unlimitedPackage);
        var unlimitedIncrement = await _handler.CreateAccessSessionAsync(NewSession(unlimitedPackage.Id));

        Assert.IsFalse(missing.Success);
        Assert.AreEqual(0, missing.NewAccessCount);

        Assert.IsFalse(alreadyLimited.Success);
        Assert.AreEqual(0, alreadyLimited.NewAccessCount);
        Assert.IsNull(alreadyLimited.MaxAccessCount);

        Assert.IsTrue(firstIncrement.Success);
        Assert.AreEqual(1, firstIncrement.NewAccessCount);
        Assert.IsTrue(secondIncrement.Success);
        Assert.AreEqual(2, secondIncrement.NewAccessCount);

        Assert.IsTrue(unlimitedIncrement.Success);
        Assert.AreEqual(6, unlimitedIncrement.NewAccessCount);
    }

    [TestMethod]
    public async Task DeleteMethods_HandleExistingAndMissingItems()
    {
        var firstPackage = CreatePackage();
        var secondPackage = CreatePackage();
        var firstContent = CreateContent(firstPackage.Id);
        var secondContent = CreateContent(secondPackage.Id);

        await _handler.StorePackageAsync(firstPackage);
        await _handler.StorePackageAsync(secondPackage);
        await _handler.StoreContentAsync(firstContent);
        await _handler.StoreContentAsync(secondContent);

        Assert.IsTrue(await _handler.DeletePackageAsync(firstPackage.Id));
        Assert.IsFalse(await _handler.DeletePackageAsync(firstPackage.Id));
        Assert.AreEqual(1, await _handler.DeletePackagesAsync([secondPackage.Id]));
        Assert.AreEqual(0, await _handler.DeletePackagesAsync([]));
    }

    [TestMethod]
    public async Task DeletePackageAsync_RemovesContents()
    {
        var package = CreatePackage();
        var content = CreateContent(package.Id) with { Size = 7 };

        await _handler.StorePackageAsync(package);
        await _handler.StoreContentAsync(content);

        Assert.IsTrue(await _handler.DeletePackageAsync(package.Id));

        Assert.IsNull(await _handler.GetContentAsync(content.Id));
        Assert.IsNull(await _handler.GetPackageShellAsync(package.Id));
    }

    [TestMethod]
    public async Task DeletePackagesAsync_RemovesContentsOfDeletedPackagesOnly()
    {
        var keptPackage = CreatePackage();
        var deletedPackage = CreatePackage();
        var alsoDeletedPackage = CreatePackage();
        var keptContent = CreateContent(keptPackage.Id) with { Size = 5 };
        var deletedContent = CreateContent(deletedPackage.Id) with { Size = 7 };
        var alsoDeletedContent = CreateContent(alsoDeletedPackage.Id) with { Size = 11 };

        await _handler.StorePackageAsync(keptPackage);
        await _handler.StorePackageAsync(deletedPackage);
        await _handler.StorePackageAsync(alsoDeletedPackage);
        await _handler.StoreContentAsync(keptContent);
        await _handler.StoreContentAsync(deletedContent);
        await _handler.StoreContentAsync(alsoDeletedContent);

        var removed = await _handler.DeletePackagesAsync(
            [deletedPackage.Id, alsoDeletedPackage.Id, Guid.NewGuid()]);

        Assert.AreEqual(2, removed);
        Assert.IsNotNull(await _handler.GetContentAsync(keptContent.Id));
        Assert.IsNotNull(await _handler.GetPackageShellAsync(keptPackage.Id));
        Assert.IsNull(await _handler.GetContentAsync(deletedContent.Id));
        Assert.IsNull(await _handler.GetContentAsync(alsoDeletedContent.Id));

        var kept = await _handler.GetPackageAsync(keptPackage.Id);
        Assert.IsNotNull(kept);
        Assert.HasCount(1, kept.Contents!);
        Assert.AreEqual(keptContent.Id, kept.Contents![0].Id);
    }

    [TestMethod]
    public async Task DeletePackagesAsync_WithNoDeletedPackages_LeavesContentsUntouched()
    {
        var package = CreatePackage();
        var content = CreateContent(package.Id);

        await _handler.StorePackageAsync(package);
        await _handler.StoreContentAsync(content);

        var removed = await _handler.DeletePackagesAsync([Guid.NewGuid()]);

        Assert.AreEqual(0, removed);
        Assert.IsNotNull(await _handler.GetPackageShellAsync(package.Id));
        Assert.IsNotNull(await _handler.GetContentAsync(content.Id));
    }

    [TestMethod]
    public async Task GetPackagesToDeleteAsync_ReturnsExpiredImmediatelyAndExhaustedAfterRetention()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-16T12:00:00+00:00", CultureInfo.InvariantCulture));
        var handler = new InMemoryContentStorageHandler(timeProvider, Options.Create(new DownloadSettings()));
        var activePackage = CreatePackage();
        var expiredPackage = CreatePackage(expiresAt: timeProvider.GetUtcNow().AddHours(-1));
        var limitedPackage = CreatePackage(maxAccessCount: 1);

        await handler.StorePackageAsync(activePackage);
        await handler.StorePackageAsync(expiredPackage);
        await handler.StorePackageAsync(limitedPackage);
        await handler.CreateAccessSessionAsync(NewSession(limitedPackage.Id, timeProvider.GetUtcNow()));

        // The exhausted package must stay servable for its retention period.
        var retained = await handler.GetPackagesToDeleteAsync();
        Assert.AreSequenceEqual([expiredPackage.Id], retained, SequenceOrder.InAnyOrder);
        Assert.DoesNotContain(limitedPackage.Id, retained);

        timeProvider.SetUtcNow(timeProvider.GetUtcNow().AddMinutes(31));

        var eligible = await handler.GetPackagesToDeleteAsync();
        Assert.AreSequenceEqual(
            [expiredPackage.Id, limitedPackage.Id], eligible, SequenceOrder.InAnyOrder);
        Assert.DoesNotContain(activePackage.Id, eligible);
    }

    [TestMethod]
    public async Task CreateAccessSessionAsync_RejectsExpiredPackage()
    {
        var package = CreatePackage(expiresAt: DateTimeOffset.Parse("2026-03-05T11:59:00+00:00", CultureInfo.InvariantCulture));
        await _handler.StorePackageAsync(package);

        var claim = await _handler.CreateAccessSessionAsync(NewSession(package.Id));

        Assert.IsFalse(claim.Success);
        var stored = await _handler.GetPackageShellAsync(package.Id);
        Assert.IsNotNull(stored);
        Assert.AreEqual(0, stored.CurrentAccessCount);
    }

    [TestMethod]
    public async Task CreateAccessSessionAsync_RecordsLastAccessAtAndStoresSession()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-16T12:00:00+00:00", CultureInfo.InvariantCulture));
        var handler = new InMemoryContentStorageHandler(timeProvider);
        var package = CreatePackage(maxAccessCount: 2);
        await handler.StorePackageAsync(package);
        var session = NewSession(package.Id, createdAt: timeProvider.GetUtcNow());

        var claimed = await handler.CreateAccessSessionAsync(session);

        Assert.IsTrue(claimed.Success);
        var stored = await handler.GetPackageShellAsync(package.Id);
        Assert.IsNotNull(stored);
        Assert.AreEqual(timeProvider.GetUtcNow(), stored.LastAccessAt);
        Assert.IsNotNull(await handler.GetAccessSessionByTokenHashAsync(session.TokenHash));
    }

    [TestMethod]
    public async Task DeletePackageAsync_QueuesContentBlobsForDeletion()
    {
        var package = CreatePackage();
        var content = CreateContent(package.Id);

        await _handler.StorePackageAsync(package);
        await _handler.StoreContentAsync(content);

        Assert.IsTrue(await _handler.DeletePackageAsync(package.Id));

        var pending = await _handler.GetPendingBlobDeletionsAsync(10);
        Assert.AreSequenceEqual([content.Url!], pending);

        await _handler.CompleteBlobDeletionAsync(content.Url!);
        Assert.IsEmpty(await _handler.GetPendingBlobDeletionsAsync(10));
    }

    [TestMethod]
    public async Task DeletePackagesAsync_DeduplicatesSharedBlobKeys()
    {
        var firstPackage = CreatePackage();
        var secondPackage = CreatePackage();
        const string sharedUrl = "blob://shared";

        await _handler.StorePackageAsync(firstPackage);
        await _handler.StorePackageAsync(secondPackage);
        await _handler.StoreContentAsync(CreateContent(firstPackage.Id) with { Url = sharedUrl });
        await _handler.StoreContentAsync(CreateContent(secondPackage.Id) with { Url = sharedUrl });

        Assert.AreEqual(2, await _handler.DeletePackagesAsync([firstPackage.Id, secondPackage.Id]));

        var pending = await _handler.GetPendingBlobDeletionsAsync(10);
        Assert.HasCount(1, pending);
        Assert.AreEqual(sharedUrl, pending[0]);
    }

    [TestMethod]
    public async Task ListPackagesAsync_ReturnsOwnPackagesNewestFirstWithAggregatesAndCursor()
    {
        var ownerId = Guid.NewGuid();
        var start = DateTimeOffset.Parse("2026-03-05T12:00:00+00:00", CultureInfo.InvariantCulture);

        var oldest = CreatePackage(createdAt: start);
        oldest = oldest with { UserId = ownerId };
        var middle = CreatePackage(createdAt: start.AddMinutes(1)) with { UserId = ownerId };
        var newest = CreatePackage(createdAt: start.AddMinutes(2), unlockVerifier: "verifier") with { UserId = ownerId };
        var foreign = CreatePackage(createdAt: start.AddMinutes(3)) with { UserId = Guid.NewGuid() };

        foreach (var package in new[] { oldest, middle, newest, foreign })
        {
            await _handler.StorePackageAsync(package);
        }

        await _handler.StoreContentAsync(CreateContent(oldest.Id) with { Size = 10 });
        await _handler.StoreContentAsync(CreateContent(oldest.Id) with { Size = 20 });

        var firstPage = await _handler.ListPackagesAsync(ownerId, cursor: null, take: 2);

        Assert.HasCount(2, firstPage.Items);
        Assert.AreEqual(newest.Id, firstPage.Items[0].Id);
        Assert.AreEqual(middle.Id, firstPage.Items[1].Id);
        Assert.IsTrue(firstPage.Items[0].IsProtected);
        Assert.IsFalse(firstPage.Items[1].IsProtected);
        Assert.IsNotNull(firstPage.NextCursor);

        var secondPage = await _handler.ListPackagesAsync(ownerId, firstPage.NextCursor, take: 2);
        Assert.HasCount(1, secondPage.Items);
        Assert.AreEqual(oldest.Id, secondPage.Items[0].Id);
        Assert.AreEqual(2, secondPage.Items[0].ItemCount);
        Assert.AreEqual(30, secondPage.Items[0].TotalSize);
        Assert.IsNull(secondPage.NextCursor);
    }

    [TestMethod]
    public async Task GetPackageShellAsync_ReturnsPackageWithoutContents()
    {
        var package = CreatePackage(unlockVerifier: "verifier");
        await _handler.StorePackageAsync(package);
        await _handler.StoreContentAsync(CreateContent(package.Id));

        var shell = await _handler.GetPackageShellAsync(package.Id);

        Assert.IsNotNull(shell);
        Assert.IsNull(shell.Contents);
        Assert.AreEqual("verifier", shell.UnlockVerifier);
    }

    private static AccessSession NewSession(Guid packageId, DateTimeOffset? createdAt = null)
        => new()
        {
            Id = Guid.NewGuid(),
            PackageId = packageId,
            ClientId = Guid.NewGuid(),
            IsGuest = true,
            TokenHash = Guid.NewGuid().ToString("N"),
            CreatedAt = createdAt ?? DateTimeOffset.Parse("2026-03-05T12:00:00+00:00", CultureInfo.InvariantCulture),
            ExpiresAt = (createdAt ?? DateTimeOffset.Parse("2026-03-05T12:00:00+00:00", CultureInfo.InvariantCulture)).AddMinutes(15)
        };

    private static ContentPackage CreatePackage(
        DateTimeOffset? createdAt = null,
        string? unlockVerifier = null,
        int? maxAccessCount = null,
        int currentAccessCount = 0,
        DateTimeOffset? expiresAt = null)
    {
        return new ContentPackage
        {
            Id = Guid.NewGuid(),
            CreatedAt = createdAt ?? DateTimeOffset.Parse("2026-03-05T12:00:00+00:00", CultureInfo.InvariantCulture),
            UserId = Guid.NewGuid(),
            UnlockVerifier = unlockVerifier,
            MaxAccessCount = maxAccessCount,
            CurrentAccessCount = currentAccessCount,
            ExpiresAt = expiresAt ?? DateTimeOffset.Parse("2099-01-01T00:00:00+00:00", CultureInfo.InvariantCulture)
        };
    }

    private static ContentModel CreateContent(Guid packageId)
    {
        return new ContentModel
        {
            Id = Guid.NewGuid(),
            PackageId = packageId,
            ContentType = ContentType.File,
            CreatedAt = DateTimeOffset.Parse("2026-03-05T12:00:00+00:00", CultureInfo.InvariantCulture),
            Size = 3,
            UserId = Guid.NewGuid(),
            Meta = "bWV0YQ",
            Url = $"blob://{Guid.NewGuid()}"
        };
    }
}
