using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace kroet.Permissions;

/// <summary>
/// Extension methods for configuring permission-based authorization in dependency injection and service providers.
/// </summary>
public static class PermissionExtensions
{
    /// <summary>
    /// Extension methods for <see cref="IServiceCollection"/> to configure permission-based authorization.
    /// </summary>
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds permission-based authorization services to the dependency injection container.
        /// </summary>
        /// <returns>The service collection for chaining additional configuration.</returns>
        /// <remarks>
        /// This method registers the following services:
        /// <list type="bullet">
        /// <item><description><see cref="IPermissionManager"/> - Manages permission registration and evaluation</description></item>
        /// <item><description><see cref="IAuthorizationHandler"/> - Handles permission-based authorization requirements</description></item>
        /// <item><description><see cref="IAuthorizationPolicyProvider"/> - Creates authorization policies dynamically for permissions</description></item>
        /// </list>
        /// </remarks>
        public IServiceCollection AddPermissionAuthorization()
        {
            // Register the permission manager as a singleton
            services.AddSingleton<IPermissionManager, DefaultPermissionManager>();
            
            // Register the authorization handler that evaluates permission requirements
            services.AddSingleton<IAuthorizationHandler, Authorization.PermissionAuthorizationHandler>();
            
            // Register the policy provider that creates policies for permissions dynamically
            services.AddSingleton<IAuthorizationPolicyProvider, Authorization.PermissionPolicyProvider>();

            return services;
        }
    }

    /// <summary>
    /// Extension methods for <see cref="IServiceProvider"/> to register permissions.
    /// </summary>
    extension(IServiceProvider services)
    {
        /// <summary>
        /// Registers a permission with the specified name and evaluation delegate.
        /// </summary>
        /// <param name="permission">The name of the permission to register (e.g., "Read.User", "Create.Resource").</param>
        /// <param name="handler">The delegate that evaluates if the permission is granted.</param>
        /// <returns>The service provider for chaining additional configuration.</returns>
        /// <exception cref="ArgumentException">Thrown when permission name is null or whitespace.</exception>
        /// <exception cref="ArgumentNullException">Thrown when handler is null.</exception>
        public IServiceProvider MapPermission(string permission, PermissionDelegate handler)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(permission);
            ArgumentNullException.ThrowIfNull(handler);

            // Get the permission manager and register the permission
            var permissionManager = services.GetRequiredService<IPermissionManager>();
            permissionManager.RegisterPermission(permission, handler);

            return services;
        }

        /// <summary>
        /// Configures permissions using the provided configuration action.
        /// </summary>
        /// <param name="configure">The action to configure the permission manager.</param>
        /// <returns>The service provider for chaining additional configuration.</returns>
        /// <remarks>
        /// This method provides access to the <see cref="IPermissionManager"/> for registering multiple permissions
        /// in a single configuration block.
        /// </remarks>
        public IServiceProvider MapPermission(Action<IPermissionManager> configure)
        {
            // Get the permission manager and execute the configuration action
            var permissionManager = services.GetRequiredService<IPermissionManager>();
            configure(permissionManager);

            return services;
        }
    }
}