using System.Security.Claims;
using Microsoft.Extensions.Logging;

namespace kroet.Permissions;

/// <summary>
/// Default implementation of <see cref="IPermissionManager"/> that manages permissions in memory.
/// </summary>
/// <param name="logger">Logger instance for tracking permission operations.</param>
/// <remarks>
/// This implementation stores permissions in a dictionary and evaluates them using registered delegates.
/// Thread-safe for read operations but not for concurrent registration operations.
/// </remarks>
internal sealed partial class DefaultPermissionManager(ILogger<DefaultPermissionManager> logger) : IPermissionManager
{
    /// <summary>
    /// Dictionary storing registered permissions and their evaluation delegates.
    /// </summary>
    private readonly Dictionary<string, PermissionDelegate> _permissions = new();

    /// <inheritdoc />
    public bool RegisterPermission(string permission, PermissionDelegate permissionDelegate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        ArgumentNullException.ThrowIfNull(permissionDelegate);

        if (!_permissions.TryAdd(permission, permissionDelegate))
        {
            LogPermissionPermissionIsAlreadyRegistered(logger, permission);
            return false;
        }

        LogPermissionPermissionRegistered(logger, permission);
        return true;
    }

    /// <inheritdoc />
    public bool RegisterPermission(Permission permission)
    {
        ArgumentNullException.ThrowIfNull(permission);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission.Name, nameof(permission));

        if (!_permissions.TryAdd(permission.Name, permission.Delegate))
        {
            LogPermissionPermissionIsAlreadyRegistered(logger, permission.Name);
            return false;
        }

        LogPermissionPermissionRegistered(logger, permission.Name);
        return true;
    }

    /// <inheritdoc />
    public bool ContainsPermission(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        return _permissions.ContainsKey(permission);
    }

    /// <inheritdoc />
    public bool IsGranted(string permission, ClaimsPrincipal claimsPrincipal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        ArgumentNullException.ThrowIfNull(claimsPrincipal);

        // Try to find the permission delegate and execute it
        if (_permissions.TryGetValue(permission, out var permissionDelegate))
        {
            return permissionDelegate(claimsPrincipal);
        }

        // Permission not registered - deny access
        return false;
    }

    /// <inheritdoc />
    public IEnumerable<string> GetPermissions()
    {
        return _permissions.Keys;
    }

    /// <inheritdoc />
    public IEnumerable<string> GetPermissions(ClaimsPrincipal claimsPrincipal)
    {
        ArgumentNullException.ThrowIfNull(claimsPrincipal);

        // Iterate through all permissions and evaluate them for the user
        foreach (var permission in _permissions)
        {
            if (permission.Value(claimsPrincipal))
            {
                yield return permission.Key;
            }
        }
    }

    /// <summary>
    /// Logs a warning when attempting to register a permission that already exists.
    /// </summary>
    [LoggerMessage(LogLevel.Warning, "Permission '{permission}' is already registered.")]
    static partial void LogPermissionPermissionIsAlreadyRegistered(ILogger<DefaultPermissionManager> logger, string permission);

    /// <summary>
    /// Logs a debug message when a permission is successfully registered.
    /// </summary>
    [LoggerMessage(LogLevel.Debug, "Permission '{permission}' registered.")]
    static partial void LogPermissionPermissionRegistered(ILogger<DefaultPermissionManager> logger, string permission);
}