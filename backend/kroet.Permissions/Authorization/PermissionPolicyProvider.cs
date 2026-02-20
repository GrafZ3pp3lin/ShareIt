using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace kroet.Permissions.Authorization;

/// <summary>
/// Custom authorization policy provider that dynamically creates authorization policies for permissions.
/// </summary>
/// <param name="options">The configured authorization options.</param>
/// <param name="logger">Logger instance for tracking policy creation.</param>
/// <remarks>
/// This provider intercepts policy requests with the format "Permission:{permissionName}" and creates
/// authorization policies on-the-fly containing a <see cref="PermissionRequirement"/>.
/// For all other policy names, it delegates to the default provider.
/// </remarks>
public sealed partial class PermissionPolicyProvider(IOptions<AuthorizationOptions> options, ILogger<PermissionPolicyProvider> logger)
    : DefaultAuthorizationPolicyProvider(options)
{
    /// <summary>
    /// Gets a value indicating whether policies created by this provider can be cached.
    /// </summary>
    /// <remarks>
    /// Returns true because permission policies are deterministic and safe to cache.
    /// </remarks>
    public override bool AllowsCachingPolicies { get; } = true;

    /// <summary>
    /// Retrieves or creates an authorization policy for the specified policy name.
    /// </summary>
    /// <param name="policyName">The name of the policy to retrieve.</param>
    /// <returns>
    /// An authorization policy containing a <see cref="PermissionRequirement"/> if the policy name
    /// matches the permission format, otherwise delegates to the base provider.
    /// </returns>
    /// <remarks>
    /// Policy names must be in the format "Permission:{permissionName}" to be handled by this provider.
    /// If the permission name is empty or whitespace, the request is delegated to the default provider.
    /// </remarks>
    public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        // Check if this is a permission-based policy
        if (!policyName.StartsWith($"{PermissionAttribute.PolicyPrefix}:", StringComparison.OrdinalIgnoreCase))
        {
            return base.GetPolicyAsync(policyName);
        }

        // Extract the permission name from the policy name (after "Permission:")
        var permission = policyName[(PermissionAttribute.PolicyPrefix.Length + 1)..];
        if (string.IsNullOrWhiteSpace(permission))
        {
            return base.GetPolicyAsync(policyName);
        }

        // Create a new policy with a permission requirement
        var policy = new AuthorizationPolicyBuilder()
            .AddRequirements(new PermissionRequirement(permission))
            .Build();

        LogCreatedPolicyForPermissionPermission(logger, permission);

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }

    /// <summary>
    /// Logs an informational message when a policy is created for a permission.
    /// </summary>
    [LoggerMessage(LogLevel.Information, "Created policy for permission '{permission}'")]
    static partial void LogCreatedPolicyForPermissionPermission(ILogger<PermissionPolicyProvider> logger, string permission);
}