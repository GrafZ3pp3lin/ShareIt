using Microsoft.AspNetCore.Authorization;

namespace kroet.Permissions.Authorization;

/// <summary>
/// Represents an authorization requirement that must be satisfied by checking if a user has a specific permission.
/// </summary>
/// <param name="Permission">The name of the permission that must be granted (e.g., "Read.User", "Create.Resource").</param>
/// <remarks>
/// This requirement is evaluated by the <see cref="PermissionAuthorizationHandler"/>.
/// </remarks>
public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;