using System.Security.Claims;

namespace kroet.Permissions;

/// <summary>
/// Delegate that evaluates if a permission is granted for a given claims principal.
/// </summary>
/// <param name="claimsPrincipal">The claims principal to evaluate the permission for.</param>
/// <returns>True if the permission is granted, false otherwise.</returns>
public delegate bool PermissionDelegate(ClaimsPrincipal claimsPrincipal);