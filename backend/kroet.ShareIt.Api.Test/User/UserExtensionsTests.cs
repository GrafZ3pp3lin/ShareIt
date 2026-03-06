using System.Security.Claims;
using kroet.ShareIt.Api.User;

namespace kroet.ShareIt.Api.Test.User;

[TestClass]
public class UserExtensionsTests
{
    [TestMethod]
    public void TryGetUserIdentity_ReturnsAuthenticatedIdentityWhenSubjectClaimExists()
    {
        var userId = Guid.NewGuid();
        var claimsPrincipal = CreateClaimsPrincipal(new Claim("sub", userId.ToString()));

        var success = claimsPrincipal.TryGetUserIdentity(Guid.NewGuid(), out var identity);

        Assert.IsTrue(success);
        Assert.AreEqual(userId, identity.Id);
        Assert.IsFalse(identity.IsGuest);
    }

    [TestMethod]
    public void TryGetUserIdentity_ReturnsGuestIdentityWhenGuestIdProvided()
    {
        var guestId = Guid.NewGuid();
        var claimsPrincipal = CreateClaimsPrincipal();

        var success = claimsPrincipal.TryGetUserIdentity(guestId, out var identity);

        Assert.IsTrue(success);
        Assert.AreEqual(guestId, identity.Id);
        Assert.IsTrue(identity.IsGuest);
    }

    [TestMethod]
    public void TryGetUserIdentity_ReturnsFalseWhenNoIdentityIsAvailable()
    {
        var claimsPrincipal = CreateClaimsPrincipal();

        var success = claimsPrincipal.TryGetUserIdentity(null, out var identity);

        Assert.IsFalse(success);
        Assert.IsNull(identity);
    }

    private static ClaimsPrincipal CreateClaimsPrincipal(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
    }
}
