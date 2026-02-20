using System.Security.Claims;

namespace kroet.Permissions;

/// <summary>
/// Manages permissions for claims principals by registering permissions and evaluating them at runtime.
/// </summary>
public interface IPermissionManager
{
    /// <summary>
    /// Registers a new permission with the specified evaluation delegate.
    /// </summary>
    /// <param name="permission">Name of the permission (e.g., "Create.Resource", "Read.User").</param>
    /// <param name="permissionDelegate">The delegate that checks if the permission is granted.</param>
    /// <returns>True if the permission was successfully registered, false if it already exists.</returns>
    public bool RegisterPermission(string permission, PermissionDelegate permissionDelegate);

    /// <summary>
    /// Registers a new permission.
    /// </summary>
    /// <param name="permission">The permission object containing the name and evaluation delegate.</param>
    /// <returns>True if the permission was successfully registered, false if it already exists.</returns>
    public bool RegisterPermission(Permission permission);

    /// <summary>
    /// Checks if a permission is registered.
    /// </summary>
    /// <param name="permission">Name of the permission.</param>
    /// <returns>True if the permission is registered, false otherwise.</returns>
    public bool ContainsPermission(string permission);

    /// <summary>
    /// Checks if a permission is granted for a given claims principal.
    /// </summary>
    /// <param name="permission">Name of the permission.</param>
    /// <param name="claimsPrincipal">ClaimsPrincipal to check the permission for.</param>
    /// <returns>True if the permission is granted for the claims principal, false otherwise (including when the permission is not registered).</returns>
    public bool IsGranted(string permission, ClaimsPrincipal claimsPrincipal);

    /// <summary>
    /// Gets all registered permission names.
    /// </summary>
    /// <returns>Enumerable of all registered permission names.</returns>
    public IEnumerable<string> GetPermissions();

    /// <summary>
    /// Gets all permissions granted to a claims principal.
    /// </summary>
    /// <param name="claimsPrincipal">ClaimsPrincipal to check the permissions for.</param>
    /// <returns>Enumerable of permission names granted to the claims principal.</returns>
    public IEnumerable<string> GetPermissions(ClaimsPrincipal claimsPrincipal);
}