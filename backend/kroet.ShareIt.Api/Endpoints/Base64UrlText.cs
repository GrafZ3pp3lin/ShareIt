using System.Buffers;
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

        // .NET 11's OperationStatus overload reports malformed input without
        // throwing, so attacker-controlled values stay off the exception path.
        var buffer = new byte[Base64Url.GetMaxDecodedLength(value.Length)];
        var status = Base64Url.DecodeFromChars(value, buffer, out _, out var written, isFinalBlock: true);
        if (status != OperationStatus.Done)
        {
            return false;
        }

        Array.Resize(ref buffer, written);
        bytes = buffer;
        return true;
    }
}
