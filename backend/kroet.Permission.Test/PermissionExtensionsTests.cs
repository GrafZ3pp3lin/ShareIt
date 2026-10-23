using System.Security.Claims;
using kroet.Permissions;
using kroet.Permissions.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace kroet.Permissions.Test;

[TestClass]
public class PermissionExtensionsTests
{
    [TestMethod]
    public void AddPermissionAuthorization_RegistersPermissionManagerAsSingleton()
    {
        using var provider = CreateProvider();

        var first = provider.GetRequiredService<IPermissionManager>();
        var second = provider.GetRequiredService<IPermissionManager>();

        Assert.AreSame(first, second);
    }

    [TestMethod]
    public void AddPermissionAuthorization_RegistersPermissionAuthorizationHandler()
    {
        using var provider = CreateProvider();

        var handler = provider.GetRequiredService<IAuthorizationHandler>();

        Assert.IsInstanceOfType<PermissionAuthorizationHandler>(handler);
    }

    [TestMethod]
    public void AddPermissionAuthorization_RegistersPermissionPolicyProvider()
    {
        using var provider = CreateProvider();

        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();

        Assert.IsInstanceOfType<PermissionPolicyProvider>(policyProvider);
    }

    [TestMethod]
    public void MapPermission_RegistersPermissionOnManager()
    {
        using var provider = CreateProvider();

        provider.MapPermission("Read.User", principal => true);

        var manager = provider.GetRequiredService<IPermissionManager>();
        Assert.IsTrue(manager.ContainsPermission("Read.User"));
        Assert.IsTrue(manager.IsGranted("Read.User", CreatePrincipal()));
    }

    [TestMethod]
    public void MapPermission_ReturnsServiceProviderForChaining()
    {
        using var provider = CreateProvider();

        var result = provider.MapPermission("Read.User", principal => true);

        Assert.AreSame(provider, result);
    }

    [TestMethod]
    public void MapPermissionWithConfigureAction_RegistersAllConfiguredPermissions()
    {
        using var provider = CreateProvider();
        var manager = provider.GetRequiredService<IPermissionManager>();

        provider.MapPermission(configure: configuration =>
        {
            configuration.RegisterPermission("Read.User", principal => true);
            configuration.RegisterPermission(new Permission("Create.User", principal => false));
        });

        Assert.IsTrue(manager.ContainsPermission("Read.User"));
        Assert.IsTrue(manager.ContainsPermission("Create.User"));
    }

    [TestMethod]
    public void MapPermissionWithConfigureAction_ReturnsServiceProviderForChaining()
    {
        using var provider = CreateProvider();

        var result = provider.MapPermission(_ => { });

        Assert.AreSame(provider, result);
    }

    [TestMethod]
    public void MapPermission_ThrowsWhenNameIsNull()
    {
        using var provider = CreateProvider();

        Assert.ThrowsExactly<ArgumentNullException>(() => provider.MapPermission(null!, principal => true));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void MapPermission_ThrowsWhenNameIsEmptyOrWhiteSpace(string name)
    {
        using var provider = CreateProvider();

        Assert.ThrowsExactly<ArgumentException>(() => provider.MapPermission(name, principal => true));
    }

    [TestMethod]
    public void MapPermission_ThrowsWhenHandlerIsNull()
    {
        using var provider = CreateProvider();

        Assert.ThrowsExactly<ArgumentNullException>(() => provider.MapPermission("Read.User", null!));
    }

    private static ServiceProvider CreateProvider() =>
        new ServiceCollection()
            .AddLogging()
            .AddPermissionAuthorization()
            .BuildServiceProvider();

    private static ClaimsPrincipal CreatePrincipal() =>
        new(new ClaimsIdentity(authenticationType: "test"));
}
