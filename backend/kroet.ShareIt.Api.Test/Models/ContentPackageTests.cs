using kroet.ShareIt.Api.Models;

namespace kroet.ShareIt.Api.Test.Models;

[TestClass]
public class ContentPackageTests
{
    [TestMethod]
    public void RemainingDownloads_ReturnsZeroWhenDownloadCountExceedsLimit()
    {
        var package = CreatePackage(maxDownloadCount: 3, currentDownloadCount: 5);

        Assert.AreEqual(0, package.RemainingDownloads);
    }

    [TestMethod]
    public void IsAccessLimitReached_ReturnsTrueWhenDownloadCountMatchesLimit()
    {
        var package = CreatePackage(maxDownloadCount: 3, currentDownloadCount: 3);

        Assert.IsTrue(package.IsAccessLimitReached);
    }

    [TestMethod]
    public void IsExpired_ReturnsTrueWhenCurrentTimeMatchesExpiry()
    {
        var expiresAt = DateTimeOffset.Parse("2026-03-05T12:00:00+00:00");
        var package = CreatePackage(expiresAt: expiresAt);

        var expired = package.IsExpired(expiresAt);

        Assert.IsTrue(expired);
    }

    private static ContentPackage CreatePackage(
        int? maxDownloadCount = null,
        int currentDownloadCount = 0,
        DateTimeOffset? expiresAt = null)
    {
        return new ContentPackage
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.Parse("2026-03-05T10:00:00+00:00"),
            MaxDownloadCount = maxDownloadCount,
            CurrentDownloadCount = currentDownloadCount,
            ExpiresAt = expiresAt
        };
    }
}
