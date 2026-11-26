using System.Security.Cryptography;

namespace kroet.ShareIt.Api.Content;

/// <summary>
/// Creates cryptographically random three-character locators for code access.
/// Registered in DI so tests can supply a deterministic sequence for collision
/// handling. The client secret of the six-character user code is generated in
/// the browser and never seen here.
/// </summary>
public interface IShortCodeGenerator
{
    /// <summary>
    /// Returns one new, normalized locator. The caller is responsible for
    /// retrying on a storage conflict.
    /// </summary>
    string Next();
}

/// <inheritdoc />
public sealed class ShortCodeGenerator : IShortCodeGenerator
{
    /// <inheritdoc />
    public string Next()
    {
        Span<char> code = stackalloc char[ShortCode.Length];
        for (var index = 0; index < code.Length; index++)
        {
            code[index] = ShortCode.Alphabet[RandomNumberGenerator.GetInt32(ShortCode.Alphabet.Length)];
        }

        return new string(code);
    }
}
