using kroet.Permissions;
using kroet.Permissions.Authorization;
using kroet.Permission.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace kroet.Permission.AspNetCore.Test;

[TestClass]
public class RequirePermissionTests
{
    [TestMethod]
    public void RequirePermission_AddsSingleRequirementToEndpointMetadata()
    {
        using var app = WebApplication.Create();

        app.MapGet("/endpoint", () => "ok").RequirePermission("Read.User");

        var metadata = GetEndpoint(app, "/endpoint").Metadata.OfType<PermissionAttribute>().ToList();
        Assert.AreEqual(1, metadata.Count);
        Assert.AreEqual("Read.User", metadata[0].Permission);
    }

    [TestMethod]
    public void RequirePermissionWithTwoPermissions_AddsBothRequirementsToEndpointMetadata()
    {
        using var app = WebApplication.Create();

        app.MapGet("/endpoint", () => "ok").RequirePermission("Read.User", "Create.User");

        var metadata = GetEndpoint(app, "/endpoint").Metadata.OfType<PermissionAttribute>().Select(attribute => attribute.Permission).ToArray();
        CollectionAssert.AreEqual(new[] { "Read.User", "Create.User" }, metadata);
    }

    [TestMethod]
    public void RequirePermissionWithManyPermissions_AddsAllRequirementsToEndpointMetadata()
    {
        using var app = WebApplication.Create();

        app.MapGet("/endpoint", () => "ok").RequirePermission(["Read.User", "Create.User", "Delete.User"]);

        var metadata = GetEndpoint(app, "/endpoint").Metadata.OfType<PermissionAttribute>().Select(attribute => attribute.Permission).ToArray();
        CollectionAssert.AreEqual(new[] { "Read.User", "Create.User", "Delete.User" }, metadata);
    }

    private static Endpoint GetEndpoint(WebApplication app, string rawRoutePattern) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .Single(endpoint =>
                ((RouteEndpoint)endpoint).RoutePattern.RawText == rawRoutePattern);
}
