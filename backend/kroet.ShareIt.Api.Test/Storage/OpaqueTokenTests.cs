using System.Security.Cryptography;
using System.Text;
using kroet.ShareIt.Api.Storage;

namespace kroet.ShareIt.Api.Test.Storage;

[TestClass]
public class OpaqueTokenTests
{
    [TestMethod]
    public void Create_ReturnsADistinctBase64UrlTokenOf32Bytes()
    {
        var first = OpaqueToken.Create();
        var second = OpaqueToken.Create();

        Assert.AreNotEqual(first, second);
        Assert.AreEqual(OpaqueToken.WellFormedLength, first.Length, "32 bytes encode to 43 base64url characters.");
        Assert.IsFalse(first.Contains('='), first);
    }

    [TestMethod]
    public void IsWellFormed_AcceptsOnlyTheCreatedShape()
    {
        var token = OpaqueToken.Create();

        Assert.IsTrue(OpaqueToken.IsWellFormed(token));
        Assert.IsTrue(OpaqueToken.IsWellFormed(new string('A', OpaqueToken.WellFormedLength)));
        Assert.IsTrue(OpaqueToken.IsWellFormed(new string('-', OpaqueToken.WellFormedLength)));
        Assert.IsTrue(OpaqueToken.IsWellFormed(new string('_', OpaqueToken.WellFormedLength)));

        Assert.IsFalse(OpaqueToken.IsWellFormed(null));
        Assert.IsFalse(OpaqueToken.IsWellFormed(""));
        Assert.IsFalse(OpaqueToken.IsWellFormed(" "));
        // One character short and one character long are both wrong, even
        // though both are valid base64url text.
        Assert.IsFalse(OpaqueToken.IsWellFormed(token[..^1]));
        Assert.IsFalse(OpaqueToken.IsWellFormed(token + "A"));
        // Padding and the standard-alphabet characters are not in the
        // base64url form the server mints.
        Assert.IsFalse(OpaqueToken.IsWellFormed(token[..^1] + "="));
        Assert.IsFalse(OpaqueToken.IsWellFormed(token[..^1] + "+"));
        Assert.IsFalse(OpaqueToken.IsWellFormed(token[..^1] + "/"));
        // Whitespace anywhere disqualifies it, not just at the edges.
        Assert.IsFalse(OpaqueToken.IsWellFormed(" " + token[1..]));
    }

    [TestMethod]
    public void Hash_ReturnsTheHexSha256OfTheToken()
    {
        const string token = "upload-token";
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        Assert.AreEqual(expected, OpaqueToken.Hash(token));
    }

    [TestMethod]
    public void Matches_AcceptsOnlyTheMatchingNonBlankToken()
    {
        var token = OpaqueToken.Create();
        var hash = OpaqueToken.Hash(token);

        Assert.IsTrue(OpaqueToken.Matches(hash, token));
        Assert.IsFalse(OpaqueToken.Matches(hash, OpaqueToken.Create()));
        Assert.IsFalse(OpaqueToken.Matches(hash, null));
        Assert.IsFalse(OpaqueToken.Matches(hash, " "));
        Assert.IsFalse(OpaqueToken.Matches(hash, "too-short"));
        Assert.IsFalse(OpaqueToken.Matches(hash, token + "A"));
        Assert.IsFalse(OpaqueToken.Matches(string.Empty, token));
    }
}
