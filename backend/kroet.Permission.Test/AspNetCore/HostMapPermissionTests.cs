using kroet.Permissions;
using kroet.Permission.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace kroet.Permission.AspNetCore.Test;

[TestClass]
public class HostMapPermissionTests
{
    [TestMethod]
    public void MapPermission_RegistersPermissionOnHostedPermissionManager()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddPermissionAuthorization();
        using var host = builder.Build();

        host.MapPermission("Read.User", principal => principal.Identity?.AuthenticationType != null);

        var manager = host.Services.GetRequiredService<IPermissionManager>();
        Assert.IsTrue(manager.ContainsPermission("Read.User"));
        Assert.IsTrue(manager.GetPermissions().SequenceEqual(new[] { "Read.User" }));
    }

    [TestMethod]
    public void MapPermissionWithConfigureAction_RegistersAllConfiguredPermissions()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddPermissionAuthorization();
        using var host = builder.Build();

        host.MapPermission(configure: configuration =>
        {
            configuration.RegisterPermission("Read.User", principal => true);
            configuration.RegisterPermission("Create.User", principal => false);
        });

        var manager = host.Services.GetRequiredService<IPermissionManager>();
        Assert.AreEqual(2, manager.GetPermissions().Count());
    }
}
