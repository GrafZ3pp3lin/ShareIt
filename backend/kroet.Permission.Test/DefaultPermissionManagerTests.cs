using System.Security.Claims;
using kroet.Permissions;
using Microsoft.Extensions.Logging.Abstractions;

namespace kroet.Permissions.Test;

[TestClass]
public class DefaultPermissionManagerTests
{
    [TestMethod]
    public void RegisterPermission_RegistersPermission()
    {
        var subject = CreateSubject();

        Assert.IsTrue(subject.RegisterPermission("Read.User", _ => true));

        Assert.IsTrue(subject.ContainsPermission("Read.User"));
        CollectionAssert.AreEqual(new[] { "Read.User" }, subject.GetPermissions().ToArray());
    }

    [TestMethod]
    public void RegisterPermission_ReturnsFalseWhenPermissionAlreadyRegistered()
    {
        var subject = CreateSubject();

        Assert.IsTrue(subject.RegisterPermission("Read.User", _ => true));
        Assert.IsFalse(subject.RegisterPermission("Read.User", _ => false));
        Assert.AreEqual(1, subject.GetPermissions().Count());
    }

    [TestMethod]
    public void RegisterPermission_KeepsOriginalDelegateWhenAlreadyRegistered()
    {
        var subject = CreateSubject();
        subject.RegisterPermission("IsAlice", principal => principal.Identity?.Name == "alice");

        subject.RegisterPermission("IsAlice", principal => principal.Identity?.Name == "bob");

        Assert.IsTrue(subject.IsGranted("IsAlice", CreatePrincipal(ClaimTypes.Name, "alice")));
    }

    [TestMethod]
    public void RegisterPermission_ThrowsWhenNameIsNull()
    {
        var subject = CreateSubject();

        Assert.ThrowsExactly<ArgumentNullException>(() => subject.RegisterPermission(null!, _ => true));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void RegisterPermission_ThrowsWhenNameIsEmptyOrWhiteSpace(string name)
    {
        var subject = CreateSubject();

        Assert.ThrowsExactly<ArgumentException>(() => subject.RegisterPermission(name, _ => true));
    }

    [TestMethod]
    public void RegisterPermission_ThrowsWhenDelegateIsNull()
    {
        var subject = CreateSubject();

        Assert.ThrowsExactly<ArgumentNullException>(() => subject.RegisterPermission("Read.User", null!));
    }

    [TestMethod]
    public void RegisterPermissionWithPermissionObject_RegistersPermission()
    {
        var subject = CreateSubject();
        var permission = new Permission("Read.User", _ => true);

        Assert.IsTrue(subject.RegisterPermission(permission));

        Assert.IsTrue(subject.ContainsPermission("Read.User"));
    }

    [TestMethod]
    public void RegisterPermissionWithPermissionObject_ReturnsFalseWhenPermissionAlreadyRegistered()
    {
        var subject = CreateSubject();
        subject.RegisterPermission("Read.User", _ => true);

        Assert.IsFalse(subject.RegisterPermission(new Permission("Read.User", _ => true)));
    }

    [TestMethod]
    public void RegisterPermissionWithPermissionObject_ThrowsWhenPermissionIsNull()
    {
        var subject = CreateSubject();

        Assert.ThrowsExactly<ArgumentNullException>(() => subject.RegisterPermission(null!));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void RegisterPermissionWithPermissionObject_ThrowsWhenNameIsEmptyOrWhiteSpace(string name)
    {
        var subject = CreateSubject();

        Assert.ThrowsExactly<ArgumentException>(() => subject.RegisterPermission(new Permission(name, _ => true)));
    }

    [TestMethod]
    public void ContainsPermission_ReturnsFalseWhenPermissionNotRegistered()
    {
        Assert.IsFalse(CreateSubject().ContainsPermission("Read.User"));
    }

    [TestMethod]
    public void ContainsPermission_ThrowsWhenNameIsNull()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => CreateSubject().ContainsPermission(null!));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void ContainsPermission_ThrowsWhenNameIsEmptyOrWhiteSpace(string name)
    {
        Assert.ThrowsExactly<ArgumentException>(() => CreateSubject().ContainsPermission(name));
    }

    [TestMethod]
    public void IsGranted_DelegatesToRegisteredPermission()
    {
        var subject = CreateSubject();
        subject.RegisterPermission("IsAlice", principal => principal.Identity?.Name == "alice");

        Assert.IsTrue(subject.IsGranted("IsAlice", CreatePrincipal(ClaimTypes.Name, "alice")));
        Assert.IsFalse(subject.IsGranted("IsAlice", CreatePrincipal(ClaimTypes.Name, "bob")));
    }

    [TestMethod]
    public void IsGranted_DelegatesToRegisteredPermissionObject()
    {
        var subject = CreateSubject();
        subject.RegisterPermission(new Permission("IsAuthenticated", principal => principal.Identity!.IsAuthenticated));

        Assert.IsTrue(subject.IsGranted("IsAuthenticated", CreatePrincipal()));
        Assert.IsFalse(subject.IsGranted("IsAuthenticated", CreateGuestPrincipal()));
    }

    [TestMethod]
    public void IsGranted_ReturnsFalseWhenPermissionNotRegistered()
    {
        var subject = CreateSubject();

        Assert.IsFalse(subject.IsGranted("Read.User", CreatePrincipal()));
    }

    [TestMethod]
    public void IsGranted_ThrowsWhenPrincipalIsNull()
    {
        var subject = CreateSubject();

        Assert.ThrowsExactly<ArgumentNullException>(() => subject.IsGranted("Read.User", null!));
    }

    [TestMethod]
    public void IsGranted_ThrowsWhenNameIsNull()
    {
        var subject = CreateSubject();

        Assert.ThrowsExactly<ArgumentNullException>(() => subject.IsGranted(null!, CreatePrincipal()));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void IsGranted_ThrowsWhenNameIsEmptyOrWhiteSpace(string name)
    {
        var subject = CreateSubject();

        Assert.ThrowsExactly<ArgumentException>(() => subject.IsGranted(name, CreatePrincipal()));
    }

    [TestMethod]
    public void GetPermissions_ReturnsAllRegisteredPermissionNames()
    {
        var subject = CreateSubject();
        subject.RegisterPermission("Read.User", _ => true);
        subject.RegisterPermission("Create.Resource", _ => true);
        subject.RegisterPermission("Delete.Resource", _ => true);

        CollectionAssert.AreEqual(
            new[] { "Create.Resource", "Delete.Resource", "Read.User" },
            subject.GetPermissions().OrderBy(name => name).ToArray());
    }

    [TestMethod]
    public void GetPermissionsForPrincipal_ReturnsOnlyGrantedPermissions()
    {
        var subject = CreateSubject();
        subject.RegisterPermission("IsAlice", principal => principal.Identity?.Name == "alice");
        subject.RegisterPermission("IsBob", principal => principal.Identity?.Name == "bob");
        subject.RegisterPermission("IsAuthenticated", principal => principal.Identity!.IsAuthenticated);

        var granted = subject.GetPermissions(CreatePrincipal(ClaimTypes.Name, "alice")).OrderBy(name => name).ToArray();

        CollectionAssert.AreEqual(new[] { "IsAlice", "IsAuthenticated" }, granted);
    }

    [TestMethod]
    public void GetPermissionsForPrincipal_ThrowsWhenPrincipalIsNull()
    {
        // GetPermissions returns a lazily evaluated enumerator, so the null check
        // only fires when the sequence is enumerated.
        Assert.ThrowsExactly<ArgumentNullException>(() => CreateSubject().GetPermissions(null!).ToArray());
    }

    private static DefaultPermissionManager CreateSubject() =>
        new(NullLogger<DefaultPermissionManager>.Instance);

    private static ClaimsPrincipal CreatePrincipal(string? claimType = null, string? claimValue = null) =>
        claimType is null
            ? new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "test"))
            : new ClaimsPrincipal(new ClaimsIdentity([new Claim(claimType, claimValue!)], authenticationType: "test"));

    private static ClaimsPrincipal CreateGuestPrincipal() =>
        new(new ClaimsIdentity());
}
