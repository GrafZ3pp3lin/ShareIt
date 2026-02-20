using Microsoft.AspNetCore.Authorization;

namespace kroet.Permissions.Authorization;

/// <summary>
/// Authorization attribute that requires a specific permission to access a controller or action.
/// </summary>
/// <param name="permission">The name of the permission required (e.g., "Read.User", "Create.Resource").</param>
/// <remarks>
/// This attribute automatically creates a policy with the format "Permission:{permission}" that is evaluated
/// by the <see cref="PermissionPolicyProvider"/> and <see cref="PermissionAuthorizationHandler"/>.
/// </remarks>
/// <example>
/// [Permission("Read.User")]
/// public IActionResult GetUser() { }
/// </example>
public class PermissionAttribute(string permission) : AuthorizeAttribute($"{PolicyPrefix}:{permission}")
{
    /// <summary>
    /// The prefix used for all permission-based authorization policies.
    /// </summary>
    public const string PolicyPrefix = "Permission";

    /// <summary>
    /// Gets or sets the name of the permission required.
    /// </summary>
    public string Permission { get; set; } = permission;
}