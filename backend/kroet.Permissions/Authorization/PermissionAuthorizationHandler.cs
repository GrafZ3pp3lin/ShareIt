using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace kroet.Permissions.Authorization;

/// <summary>
/// Authorization handler that evaluates permission-based authorization requirements.
/// This handler checks if the current user has been granted a specific permission.
/// </summary>
/// <param name="permissionManager">Service responsible for checking if permissions are granted to users.</param>
/// <param name="logger">Logger instance for tracking authorization decisions.</param>
public partial class PermissionAuthorizationHandler(IPermissionManager permissionManager, ILogger<PermissionAuthorizationHandler> logger)
    : AuthorizationHandler<PermissionRequirement>
{
    /// <summary>
    /// Evaluates whether the current user meets the permission requirement.
    /// </summary>
    /// <param name="context">The authorization context containing information about the user and resource.</param>
    /// <param name="requirement">The permission requirement that must be satisfied.</param>
    /// <returns>A completed task. Authorization succeeds if the permission is granted, fails otherwise.</returns>
    /// <remarks>
    /// If the permission is not granted, the method returns without calling context.Succeed(),
    /// which causes the authorization to fail automatically. There is no need to explicitly call context.Fail().
    /// </remarks>
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        // Check if the user has the required permission
        if (!permissionManager.IsGranted(requirement.Permission, context.User))
        {
            // Permission denied - log and return without succeeding the requirement
            LogPermissionDeniedForUser(logger, requirement.Permission, context.User.Identity?.Name);
            return Task.CompletedTask;
        }

        // Permission granted - log and mark the requirement as satisfied
        LogPermissionPermissionGrantedForUserUser(logger, requirement.Permission, context.User.Identity?.Name);
        context.Succeed(requirement);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Logs a debug message when a permission is granted to a user.
    /// </summary>
    [LoggerMessage(LogLevel.Debug, "Permission '{permission}' granted for user '{user}'.")]
    static partial void LogPermissionPermissionGrantedForUserUser(ILogger<PermissionAuthorizationHandler> logger, string permission, string? user);

    /// <summary>
    /// Logs a warning message when a permission is denied to a user.
    /// This helps track unauthorized access attempts.
    /// </summary>
    [LoggerMessage(LogLevel.Warning, "Permission '{permission}' denied for user '{user}'.")]
    static partial void LogPermissionDeniedForUser(ILogger<PermissionAuthorizationHandler> logger, string permission, string? user);
}