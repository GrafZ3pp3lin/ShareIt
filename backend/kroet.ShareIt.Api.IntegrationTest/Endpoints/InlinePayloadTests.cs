using System.Net;
using System.Text;
using kroet.ShareIt.Api.Content.Dtos;
using kroet.ShareIt.Api.IntegrationTest.Infrastructure;

namespace kroet.ShareIt.Api.IntegrationTest.Endpoints;

/// <summary>
/// The transport decision is size-based, never type-based: items within the
/// per-item limit and the response budget come back as base64url payloads,
/// everything else through the access session. Metadata is always inline.
/// </summary>
[TestClass]
public sealed class InlinePayloadTests : ShareItApiTestBase
{
    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["DownloadSettings:InlinePayloadLimitBytes"] = "1024",
        ["DownloadSettings:InlinePayloadBudgetBytes"] = "1024"
    };

    [TestMethod]
    public async Task GetPackage_SmallItem_CarriesPayloadInsteadOfToken()
    {
        var guestId = Guid.NewGuid();
        var (packageId, _) = await CreatePackageWithItemAsync(bytes: new byte[100], guestId: guestId);

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);
        var item = details.Items.Single();

        Assert.IsNotNull(item.Payload);
        Assert.IsFalse(string.IsNullOrEmpty(details.AccessSession.Token));
    }

    [TestMethod]
    public async Task GetPackage_ItemAboveLimit_UsesAccessSession()
    {
        var guestId = Guid.NewGuid();
        var (packageId, _) = await CreatePackageWithItemAsync(bytes: new byte[2000], guestId: guestId);

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);
        var item = details.Items.Single();

        Assert.IsNull(item.Payload);
        Assert.IsFalse(string.IsNullOrEmpty(details.AccessSession.Token));
    }

    [TestMethod]
    public async Task GetPackage_LargeTextItem_UsesTokenLikeFiles()
    {
        var guestId = Guid.NewGuid();
        var textBytes = Encoding.UTF8.GetBytes(new string('a', 2000));
        var (packageId, _) = await CreatePackageWithItemAsync(kind: "text", bytes: textBytes, guestId: guestId);

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);
        var item = details.Items.Single();

        Assert.AreEqual("text", item.Kind);
        Assert.IsNull(item.Payload);
        Assert.IsFalse(string.IsNullOrEmpty(details.AccessSession.Token));
    }

    [TestMethod]
    public async Task GetPackage_WhenBudgetIsExhausted_FallsBackToTokensForTheRest()
    {
        var guestId = Guid.NewGuid();
        var packageId = await CreatePackageAsync(guestId: guestId);

        var firstMeta = MetaFor("first.bin", "application/octet-stream");
        var secondMeta = MetaFor("second.bin", "application/octet-stream");

        var first = await SendUploadItemAsync(packageId, "file", new byte[600], firstMeta, guestId: guestId);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);

        // Stable ordering by creation: the first item spends most of the
        // 1 KB budget, so the second must be served through the access session.
        TimeProvider.UtcNow = TimeProvider.UtcNow.AddMinutes(1);
        var second = await SendUploadItemAsync(packageId, "file", new byte[600], secondMeta, guestId: guestId);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);

        Assert.HasCount(2, details.Items);
        var firstItem = details.Items[0];
        var secondItem = details.Items[1];
        Assert.AreEqual(firstMeta, firstItem.Meta);
        Assert.IsNotNull(firstItem.Payload);

        Assert.AreEqual(secondMeta, secondItem.Meta, "Meta is always inline, outside the budget.");
        Assert.IsNull(secondItem.Payload);
        Assert.IsFalse(string.IsNullOrEmpty(details.AccessSession.Token));
    }
}

/// <summary>
/// With a zero payload budget every item is served through the access session, but the
/// metadata still travels inline because it is needed to render the list.
/// </summary>
[TestClass]
public sealed class ZeroBudgetInlinePayloadTests : ShareItApiTestBase
{
    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["DownloadSettings:InlinePayloadLimitBytes"] = "16384",
        ["DownloadSettings:InlinePayloadBudgetBytes"] = "0"
    };

    [TestMethod]
    public async Task GetPackage_ZeroBudget_ServesSessionAndKeepsMetaInline()
    {
        var guestId = Guid.NewGuid();
        var meta = MetaFor("note.txt", "text/plain");
        var (packageId, _) = await CreatePackageWithItemAsync(
            kind: "text", bytes: "some shared text"u8.ToArray(), meta: meta, guestId: guestId);

        var details = await GetPackageDetailsAsync(packageId, guestId: guestId);
        var item = details.Items.Single();

        Assert.IsNull(item.Payload);
        Assert.IsFalse(string.IsNullOrEmpty(details.AccessSession.Token));
        Assert.AreEqual(meta, item.Meta);
    }
}
