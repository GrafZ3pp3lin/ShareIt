using kroet.Permissions.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace kroet.Permissions.Test.Authorization;

[TestClass]
public class PermissionPolicyProviderTests
{
    [TestMethod]
    public void AllowsCachingPolicies_IsEnabled()
    {
        Assert.IsTrue(CreateSubject().AllowsCachingPolicies);
    }

    [TestMethod]
    public async Task GetPolicyAsync_CreatesPolicyForPermissionPolicyName()
    {
        var policy = await CreateSubject().GetPolicyAsync("Permission:Read.User");

        Assert.IsNotNull(policy);
        var requirement = Assert.IsInstanceOfType<PermissionRequirement>(policy.Requirements.Single());
        Assert.AreEqual("Read.User", requirement.Permission);
    }

    [TestMethod]
    public async Task GetPolicyAsync_IsCaseInsensitiveOnPolicyPrefix()
    {
        var policy = await CreateSubject().GetPolicyAsync("permission:Read.User");

        Assert.IsNotNull(policy);
        var requirement = Assert.IsInstanceOfType<PermissionRequirement>(policy.Requirements.Single());
        Assert.AreEqual("Read.User", requirement.Permission);
    }

    [TestMethod]
    [DataRow("Read.User")]
    [DataRow("Permission")]
    [DataRow("Permission:")]
    public async Task GetPolicyAsync_DelegatesToDefaultProviderForOtherPolicyNames(string policyName)
    {
        var policy = await CreateSubject().GetPolicyAsync(policyName);

        Assert.IsNull(policy);
    }

    private static PermissionPolicyProvider CreateSubject() =>
        new(Options.Create(new AuthorizationOptions()), NullLogger<PermissionPolicyProvider>.Instance);
}
