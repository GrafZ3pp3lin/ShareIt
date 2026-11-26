using System.Net;
using System.Net.Sockets;

namespace kroet.ShareIt.Api.Services;

/// <summary>
/// Builds rate-limit partition keys from a remote address. IPv6 clients can
/// rotate freely within their /64, so the whole prefix shares one budget.
/// </summary>
public static class RemoteIpKey
{
    public static string From(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            Array.Clear(bytes, 8, 8);
            return $"{new IPAddress(bytes)}/64";
        }

        return address.ToString();
    }
}
