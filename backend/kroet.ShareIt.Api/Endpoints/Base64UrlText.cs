using System.Buffers.Text;

namespace kroet.ShareIt.Api.Content;

/// <summary>
/// Strict base64url decoding helper. Both the unlock verifier and the opaque
/// per-item metadata are transported as base64url text.
/// </summary>
internal static class Base64UrlText
{
    public static bool TryDecode(string? value, out byte[] bytes)
    {
        bytes = [];

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            bytes = Base64Url.DecodeFromChars(value);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return false;
        }
    }
}
