using kroet.Permissions.Authorization;

namespace kroet.Permissions.Test.Authorization;

[TestClass]
public class PermissionAttributeTests
{
    [TestMethod]
    public void PermissionAttribute_UsesPermissionPolicyName()
    {
        var attribute = new PermissionAttribute("Read.User");

        Assert.AreEqual("Permission:Read.User", attribute.Policy);
        Assert.AreEqual("Read.User", attribute.Permission);
    }
}
