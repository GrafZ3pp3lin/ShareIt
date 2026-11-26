namespace kroet.ShareIt.Api.Storage;

/// <summary>
/// Raised when a package insert loses a race for the same package id. The
/// caller can re-read the row and apply the idempotent-create rules.
/// </summary>
public sealed class PackageIdConflictException(Exception? innerException = null)
    : Exception("A package with this id already exists.", innerException)
{
}
