using System.Diagnostics.CodeAnalysis;

namespace kroet.Permissions;

/// <summary>
/// Represents a permission definition containing a name and evaluation logic.
/// </summary>
public record Permission
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Permission"/> class.
    /// </summary>
    public Permission()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Permission"/> class with the specified name and delegate.
    /// </summary>
    /// <param name="name">The name of the permission (e.g., "Read.User", "Create.Resource").</param>
    /// <param name="delegate">The delegate that evaluates if the permission is granted.</param>
    [SetsRequiredMembers]
    public Permission(string name, PermissionDelegate @delegate)
    {
        Name = name;
        Delegate = @delegate;
    }

    /// <summary>
    /// Gets or initializes the name of the permission (e.g., "Read.User", "Create.Resource").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets or initializes the delegate that checks if the permission is granted for a given claims principal.
    /// </summary>
    public required PermissionDelegate Delegate { get; init; }
}