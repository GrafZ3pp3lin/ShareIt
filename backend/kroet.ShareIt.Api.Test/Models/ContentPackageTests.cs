using kroet.ShareIt.Api.Models;
using System.Globalization;

namespace kroet.ShareIt.Api.Test.Models;

[TestClass]
public class ContentPackageTests
{
    [TestMethod]
    public void RemainingAccesses_ReturnsZeroWhenDownloadCountExceedsLimit()
    {
        var package = CreatePackage(maxAccessCount: 3, currentAccessCount: 5);

        Assert.AreEqual(0, package.RemainingAccesses);
    }

    [TestMethod]
    public void IsAccessLimitReached_ReturnsTrueWhenDownloadCountMatchesLimit()
    {
        var package = CreatePackage(maxAccessCount: 3, currentAccessCount: 3);

        Assert.IsTrue(package.IsAccessLimitReached);
    }

    [TestMethod]
    public void IsExpired_ReturnsTrueWhenCurrentTimeMatchesExpiry()
    {
        var expiresAt = DateTimeOffset.Parse("2026-03-05T12:00:00+00:00", CultureInfo.InvariantCulture);
        var package = CreatePackage(expiresAt: expiresAt);

        var expired = package.IsExpired(expiresAt);

        Assert.IsTrue(expired);
    }

    [TestMethod]
    public void IsExpired_ReturnsFalseBeforeExpiry()
    {
        var expiresAt = DateTimeOffset.Parse("2026-03-05T12:00:00+00:00", CultureInfo.InvariantCulture);
        var package = CreatePackage(expiresAt: expiresAt);

        var expired = package.IsExpired(expiresAt.AddMinutes(-1));

        Assert.IsFalse(expired);
    }

    private static ContentPackage CreatePackage(
        int? maxAccessCount = null,
        int currentAccessCount = 0,
        DateTimeOffset? expiresAt = null)
    {
        return new ContentPackage
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.Parse("2026-03-05T10:00:00+00:00", CultureInfo.InvariantCulture),
            MaxAccessCount = maxAccessCount,
            CurrentAccessCount = currentAccessCount,
            ExpiresAt = expiresAt ?? DateTimeOffset.Parse("2026-03-05T12:00:00+00:00", CultureInfo.InvariantCulture)
        };
    }
}
