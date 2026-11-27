using System.Security.Claims;
using kroet.ShareIt.Api.User;

namespace kroet.ShareIt.Api.Test.User;

[TestClass]
public class UserExtensionsTests
{
    [TestMethod]
    public void GetUserId_ReturnsTheSubjectClaimWhenItIsAGuid()
    {
        var userId = Guid.NewGuid();
        var claimsPrincipal = CreateClaimsPrincipal(new Claim("sub", userId.ToString()));

        Assert.AreEqual(userId, claimsPrincipal.GetUserId());
        Assert.IsTrue(claimsPrincipal.TryGetUserId(out var resolvedUserId));
        Assert.AreEqual(userId, resolvedUserId);
    }

    [TestMethod]
    public void GetUserId_ReturnsNullForAnUnauthenticatedPrincipal()
    {
        var claimsPrincipal = CreateClaimsPrincipal();

        Assert.IsNull(claimsPrincipal.GetUserId());
        Assert.IsFalse(claimsPrincipal.TryGetUserId(out var resolvedUserId));
        Assert.AreEqual(Guid.Empty, resolvedUserId);
    }

    [TestMethod]
    public void GetUserId_ReturnsNullForANonGuidSubject()
    {
        var claimsPrincipal = CreateClaimsPrincipal(new Claim("sub", "not-a-guid"));

        Assert.IsNull(claimsPrincipal.GetUserId());
        Assert.IsFalse(claimsPrincipal.TryGetUserId(out _));
    }

    private static ClaimsPrincipal CreateClaimsPrincipal(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
    }
}
