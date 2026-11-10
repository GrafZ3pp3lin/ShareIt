using kroet.ShareIt.Api.Models;
using kroet.ShareIt.Api.Storage.InMemory;
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
        await _handler.StoreContentsAsync([secondContent]);
        await _handler.StoreContentsAsync([]);

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
    public async Task GetPackagesAsync_ReturnsRequestedPackagesWithAndWithoutContent()
    {
        var firstPackage = CreatePackage();
        var secondPackage = CreatePackage();
        var firstContent = CreateContent(firstPackage.Id);
        var secondContent = CreateContent(secondPackage.Id);

        await _handler.StorePackageAsync(firstPackage);
        await _handler.StorePackageAsync(secondPackage);
        await _handler.StoreContentsAsync([firstContent, secondContent]);

        var withContent = await _handler.GetPackagesAsync(
            [firstPackage.Id, secondPackage.Id],
            includeContent: true);
        var withoutContent = await _handler.GetPackagesAsync(
            [firstPackage.Id, secondPackage.Id],
            includeContent: false);
        var empty = await _handler.GetPackagesAsync([]);

        Assert.HasCount(2, withContent);
        Assert.HasCount(1, withContent.Single(package => package.Id == firstPackage.Id).Contents!);
        Assert.HasCount(1, withContent.Single(package => package.Id == secondPackage.Id).Contents!);
        Assert.HasCount(2, withoutContent);
        Assert.IsTrue(withoutContent.All(package => package.Contents is null));
        Assert.IsEmpty(empty);
    }

    [TestMethod]
    public async Task GetContentAndGetContents_ReturnMatchingItems()
    {
        var firstContent = CreateContent(Guid.NewGuid());
        var secondContent = CreateContent(Guid.NewGuid());

        await _handler.StoreContentAsync(firstContent);
        await _handler.StoreContentAsync(secondContent);

        var found = await _handler.GetContentAsync(firstContent.Id);
        var missing = await _handler.GetContentAsync(Guid.NewGuid());
        var contents = await _handler.GetContentsAsync([firstContent.Id, secondContent.Id]);
        var empty = await _handler.GetContentsAsync([]);

        Assert.AreSame(firstContent, found);
        Assert.IsNull(missing);
        Assert.HasCount(2, contents);
        Assert.IsEmpty(empty);
    }

    [TestMethod]
    public async Task IncrementPackageAccessCountAsync_HandlesMissingLimitReachedUnlimitedAndSuccess()
    {
        var missing = await _handler.IncrementPackageAccessCountAsync(Guid.NewGuid());

        var limitedPackage = CreatePackage(maxDownloadCount: 2, currentDownloadCount: 2);
        await _handler.StorePackageAsync(limitedPackage);
        var alreadyLimited = await _handler.IncrementPackageAccessCountAsync(limitedPackage.Id);

        var successfulPackage = CreatePackage(maxDownloadCount: 2, currentDownloadCount: 0);
        await _handler.StorePackageAsync(successfulPackage);
        var firstIncrement = await _handler.IncrementPackageAccessCountAsync(successfulPackage.Id);
        var secondIncrement = await _handler.IncrementPackageAccessCountAsync(successfulPackage.Id);

        var unlimitedPackage = CreatePackage(maxDownloadCount: null, currentDownloadCount: 5);
        await _handler.StorePackageAsync(unlimitedPackage);
        var unlimitedIncrement = await _handler.IncrementPackageAccessCountAsync(unlimitedPackage.Id);

        Assert.IsFalse(missing.Success);
        Assert.AreEqual(0, missing.NewCount);

        Assert.IsFalse(alreadyLimited.Success);
        Assert.AreEqual(2, alreadyLimited.NewCount);
        Assert.IsTrue(alreadyLimited.LimitReached);

        Assert.IsTrue(firstIncrement.Success);
        Assert.AreEqual(1, firstIncrement.NewCount);
        Assert.IsFalse(firstIncrement.LimitReached);
        Assert.IsTrue(secondIncrement.Success);
        Assert.AreEqual(2, secondIncrement.NewCount);
        Assert.IsTrue(secondIncrement.LimitReached);

        Assert.IsTrue(unlimitedIncrement.Success);
        Assert.AreEqual(6, unlimitedIncrement.NewCount);
        Assert.IsFalse(unlimitedIncrement.LimitReached);
    }

    [TestMethod]
    public async Task DeleteAndExistsMethods_HandleExistingAndMissingItems()
    {
        var firstPackage = CreatePackage();
        var secondPackage = CreatePackage();
        var firstContent = CreateContent(firstPackage.Id);
        var secondContent = CreateContent(secondPackage.Id);

        await _handler.StorePackageAsync(firstPackage);
        await _handler.StorePackageAsync(secondPackage);
        await _handler.StoreContentsAsync([firstContent, secondContent]);

        Assert.IsTrue(await _handler.ExistsPackageAsync(firstPackage.Id));
        Assert.IsFalse(await _handler.ExistsPackageAsync(Guid.NewGuid()));
        Assert.IsTrue(await _handler.ExistsContentAsync(firstContent.Id));
        Assert.IsFalse(await _handler.ExistsContentAsync(Guid.NewGuid()));

        Assert.IsTrue(await _handler.DeleteContentAsync(firstContent.Id));
        Assert.IsFalse(await _handler.DeleteContentAsync(firstContent.Id));
        Assert.AreEqual(1, await _handler.DeleteContentsAsync([secondContent.Id]));
        Assert.AreEqual(0, await _handler.DeleteContentsAsync([]));

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

        Assert.IsFalse(await _handler.ExistsPackageAsync(package.Id));
        Assert.IsFalse(await _handler.ExistsContentAsync(content.Id));
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
        await _handler.StoreContentsAsync([keptContent, deletedContent, alsoDeletedContent]);

        var removed = await _handler.DeletePackagesAsync(
            [deletedPackage.Id, alsoDeletedPackage.Id, Guid.NewGuid()]);

        Assert.AreEqual(2, removed);
        Assert.IsTrue(await _handler.ExistsContentAsync(keptContent.Id));
        Assert.IsTrue(await _handler.ExistsPackageAsync(keptPackage.Id));
        Assert.IsFalse(await _handler.ExistsContentAsync(deletedContent.Id));
        Assert.IsFalse(await _handler.ExistsContentAsync(alsoDeletedContent.Id));

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
        Assert.IsTrue(await _handler.ExistsPackageAsync(package.Id));
        Assert.IsTrue(await _handler.ExistsContentAsync(content.Id));
    }

    [TestMethod]
    public async Task GetPackagesToDeleteAsync_ReturnsExpiredAndAccessLimitedPackages()
    {
        var activePackage = CreatePackage();
        var expiredPackage = CreatePackage(expiresAt: DateTimeOffset.UtcNow.AddHours(-1));
        var limitedPackage = CreatePackage(maxDownloadCount: 1, currentDownloadCount: 1);

        await _handler.StorePackageAsync(activePackage);
        await _handler.StorePackageAsync(expiredPackage);
        await _handler.StorePackageAsync(limitedPackage);

        var packagesToDelete = await _handler.GetPackagesToDeleteAsync();

        Assert.HasCount(2, packagesToDelete);
        CollectionAssert.AreEquivalent(
            new[] { expiredPackage.Id, limitedPackage.Id },
            packagesToDelete.ToArray());
        Assert.DoesNotContain(activePackage.Id, packagesToDelete);
    }

    [TestMethod]
    public async Task ListPackagesAsync_ReturnsOwnPackagesNewestFirstWithAggregatesAndCursor()
    {
        var ownerId = Guid.NewGuid();
        var start = DateTimeOffset.Parse("2026-03-05T12:00:00+00:00");

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

    private static ContentPackage CreatePackage(
        DateTimeOffset? createdAt = null,
        string? unlockVerifier = null,
        int? maxDownloadCount = null,
        int currentDownloadCount = 0,
        DateTimeOffset? expiresAt = null)
    {
        return new ContentPackage
        {
            Id = Guid.NewGuid(),
            CreatedAt = createdAt ?? DateTimeOffset.Parse("2026-03-05T12:00:00+00:00"),
            UserId = Guid.NewGuid(),
            UnlockVerifier = unlockVerifier,
            MaxDownloadCount = maxDownloadCount,
            CurrentDownloadCount = currentDownloadCount,
            ExpiresAt = expiresAt
        };
    }

    private static ExternalContent CreateContent(Guid packageId)
    {
        return new ExternalContent(
            new ContentModel
            {
                Id = Guid.NewGuid(),
                PackageId = packageId,
                ContentType = ContentType.File,
                CreatedAt = DateTimeOffset.Parse("2026-03-05T12:00:00+00:00"),
                Size = 3,
                UserId = Guid.NewGuid(),
                Meta = "bWV0YQ"
            },
            $"blob://{Guid.NewGuid()}");
    }
}
