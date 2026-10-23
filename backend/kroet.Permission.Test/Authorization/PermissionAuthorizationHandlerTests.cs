using System.Security.Claims;
using kroet.Permissions;
using kroet.Permissions.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace kroet.Permissions.Test.Authorization;

[TestClass]
public class PermissionAuthorizationHandlerTests
{
    [TestMethod]
    public async Task HandleAsync_SucceedsWhenPermissionIsGranted()
    {
        using var provider = CreateProvider(principal => principal.Identity?.Name == "alice");
        var handler = provider.GetRequiredService<IAuthorizationHandler>();
        var context = new AuthorizationHandlerContext(
            [new PermissionRequirement("Read.User")],
            CreatePrincipal("alice"),
            resource: null);

        await handler.HandleAsync(context);

        Assert.IsTrue(context.HasSucceeded);
        Assert.IsFalse(context.HasFailed);
    }

    [TestMethod]
    public async Task HandleAsync_DoesNotSucceedWhenPermissionIsDenied()
    {
        using var provider = CreateProvider(principal => principal.Identity?.Name == "alice");
        var handler = provider.GetRequiredService<IAuthorizationHandler>();
        var context = new AuthorizationHandlerContext(
            [new PermissionRequirement("Read.User")],
            CreatePrincipal("bob"),
            resource: null);

        await handler.HandleAsync(context);

        Assert.IsFalse(context.HasSucceeded);
        Assert.IsFalse(context.HasFailed);
    }

    [TestMethod]
    public async Task HandleAsync_DoesNotSucceedWhenPermissionIsNotRegistered()
    {
        using var provider = CreateProvider(_ => true);
        var handler = provider.GetRequiredService<IAuthorizationHandler>();
        var context = new AuthorizationHandlerContext(
            [new PermissionRequirement("Delete.User")],
            CreatePrincipal("alice"),
            resource: null);

        await handler.HandleAsync(context);

        Assert.IsFalse(context.HasSucceeded);
        Assert.IsFalse(context.HasFailed);
    }

    private static ServiceProvider CreateProvider(PermissionDelegate permission)
    {
        var provider = new ServiceCollection()
            .AddLogging()
            .AddPermissionAuthorization()
            .BuildServiceProvider();
        provider.MapPermission("Read.User", permission);
        return provider;
    }

    private static ClaimsPrincipal CreatePrincipal(string name) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, name)], authenticationType: "test"));
}
