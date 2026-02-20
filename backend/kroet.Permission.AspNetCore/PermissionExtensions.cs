using kroet.Permissions;
using kroet.Permissions.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace kroet.Permission.AspNetCore;

/// <summary>
/// Extension methods for integrating permissions with ASP.NET Core endpoints and hosts.
/// </summary>
public static class PermissionExtensions
{
    /// <summary>
    /// Extension methods for <see cref="IEndpointConventionBuilder"/> to add permission-based authorization.
    /// </summary>
    extension<TBuilder>(TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        /// <summary>
        /// Requires the specified permission for accessing the endpoint.
        /// </summary>
        /// <param name="permission">The name of the permission required (e.g., "Read.User", "Create.Resource").</param>
        /// <returns>The builder for chaining additional configuration.</returns>
        /// <exception cref="ArgumentNullException">Thrown when builder is null.</exception>
        public TBuilder RequirePermission(string permission)
        {
            ArgumentNullException.ThrowIfNull(builder);

            return builder.RequireAuthorization(new PermissionAttribute(permission));
        }
        
        /// <summary>
        /// Requires both of the specified permissions for accessing the endpoint.
        /// </summary>
        /// <param name="permission1">The name of the first permission required.</param>
        /// <param name="permission2">The name of the second permission required.</param>
        /// <returns>The builder for chaining additional configuration.</returns>
        /// <exception cref="ArgumentNullException">Thrown when builder is null.</exception>
        public TBuilder RequirePermission(string permission1, string permission2)
        {
            ArgumentNullException.ThrowIfNull(builder);

            return builder.RequireAuthorization(new PermissionAttribute(permission1), new PermissionAttribute(permission2));
        }
        
        /// <summary>
        /// Requires all the specified permissions for accessing the endpoint.
        /// </summary>
        /// <param name="permissions">The names of the permissions required.</param>
        /// <returns>The builder for chaining additional configuration.</returns>
        /// <exception cref="ArgumentNullException">Thrown when builder is null.</exception>
        public TBuilder RequirePermission(params string[] permissions)
        {
            ArgumentNullException.ThrowIfNull(builder);

            var attributes = permissions.Select<string, IAuthorizeData>(p => new PermissionAttribute(p)).ToArray();
            return builder.RequireAuthorization(attributes);
        }
    }

    /// <summary>
    /// Extension methods for <see cref="IHost"/> to register permissions.
    /// </summary>
    extension(IHost host)
    {
        /// <summary>
        /// Registers a permission with the specified name and evaluation delegate.
        /// </summary>
        /// <param name="permission">The name of the permission to register.</param>
        /// <param name="handler">The delegate that evaluates if the permission is granted.</param>
        /// <returns>The host for chaining additional configuration.</returns>
        /// <exception cref="ArgumentException">Thrown when permission name is null or whitespace.</exception>
        /// <exception cref="ArgumentNullException">Thrown when handler is null.</exception>
        public IHost MapPermission(string permission, PermissionDelegate handler)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(permission);
            ArgumentNullException.ThrowIfNull(handler);

            var permissionManager = host.Services.GetRequiredService<IPermissionManager>();
            permissionManager.RegisterPermission(permission, handler);

            return host;
        }

        /// <summary>
        /// Configures permissions using the provided configuration action.
        /// </summary>
        /// <param name="configure">The action to configure the permission manager.</param>
        /// <returns>The host for chaining additional configuration.</returns>
        /// <remarks>
        /// This method provides access to the <see cref="IPermissionManager"/> for registering multiple permissions.
        /// </remarks>
        public IHost MapPermission(Action<IPermissionManager> configure)
        {
            var permissionManager = host.Services.GetRequiredService<IPermissionManager>();
            configure(permissionManager);

            return host;
        }
    }
}