namespace kroet.ShareIt.Api.Storage;

/// <summary>
/// Raised when the generated locator is already taken. The caller may retry
/// with a different locator; no package row was written.
/// </summary>
public sealed class ShortCodeConflictException(Exception? innerException = null)
    : Exception("The generated short code locator is already in use.", innerException)
{
}
