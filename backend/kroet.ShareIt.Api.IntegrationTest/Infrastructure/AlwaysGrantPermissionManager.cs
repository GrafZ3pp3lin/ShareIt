using System.Security.Claims;
using kroet.Permissions;
using PermissionModel = kroet.Permissions.Permission;

namespace kroet.ShareIt.Api.IntegrationTest.Infrastructure;

/// <summary>
/// Permission manager that accepts every permission check. Used to reach
/// defensive endpoint branches that the normal permission wiring blocks before
/// the handler runs.
/// </summary>
internal sealed class AlwaysGrantPermissionManager : IPermissionManager
{
    private readonly HashSet<string> _permissions = [];

    public bool RegisterPermission(string permission, PermissionDelegate permissionDelegate)
        => _permissions.Add(permission);

    public bool RegisterPermission(PermissionModel permission)
        => _permissions.Add(permission.Name);

    public bool ContainsPermission(string permission) => _permissions.Contains(permission);

    public bool IsGranted(string permission, ClaimsPrincipal claimsPrincipal) => true;

    public IEnumerable<string> GetPermissions() => _permissions;

    public IEnumerable<string> GetPermissions(ClaimsPrincipal claimsPrincipal) => _permissions;
}
