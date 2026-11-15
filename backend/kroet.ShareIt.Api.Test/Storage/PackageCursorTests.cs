using System.Buffers.Text;
using System.Text;
using kroet.ShareIt.Api.Storage;

namespace kroet.ShareIt.Api.Test.Storage;

[TestClass]
public class PackageCursorTests
{
    [TestMethod]
    public void EncodeThenTryDecode_RoundTripsCreatedAtAndId()
    {
        var createdAt = DateTimeOffset.Parse("2026-03-05T12:34:56.789+00:00");
        var id = Guid.NewGuid();

        var cursor = PackageCursor.Encode(createdAt, id);

        Assert.IsTrue(PackageCursor.TryDecode(cursor, out var decodedCreatedAt, out var decodedId));
        Assert.AreEqual(createdAt, decodedCreatedAt);
        Assert.AreEqual(id, decodedId);
    }

    [TestMethod]
    public void TryDecode_RejectsMalformedCursors()
    {
        Assert.IsFalse(PackageCursor.TryDecode(null, out _, out _));
        Assert.IsFalse(PackageCursor.TryDecode("", out _, out _));
        Assert.IsFalse(PackageCursor.TryDecode("not base64!", out _, out _));

        var noSeparator = Base64Url.EncodeToString("no-separator"u8);
        Assert.IsFalse(PackageCursor.TryDecode(noSeparator, out _, out _));
    }

    [TestMethod]
    public void TryDecode_RejectsTicksOutsideDateTimeRange()
    {
        // A huge tick value used to pass long.TryParse and then throw from the
        // DateTimeOffset constructor.
        var cursor = Base64Url.EncodeToString(
            Encoding.UTF8.GetBytes($"{long.MaxValue}:{Guid.NewGuid():N}"));

        Assert.IsFalse(PackageCursor.TryDecode(cursor, out _, out _));
    }
}
