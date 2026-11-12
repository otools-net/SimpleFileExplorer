using System.Net;
using System.Net.Sockets;

namespace Sfe.Api.Security;

/// <summary>
/// The key a limit counts a network address under. An IPv4 address is one visitor; an IPv6 visitor owns at least a
/// whole /64 and picks a new address in it for every request, so its key is its network: counting the full address
/// would never limit it.
/// </summary>
public static class AddressKeys
{
    /// <summary>Bytes of an IPv6 address naming the network a provider gives one customer (/64).</summary>
    private const int NetworkBytes = 8;

    /// <summary>The limit key of the request's visitor; empty when the connection has no address (tests, pipes).</summary>
    public static string Of(HttpContext context) => Of(context.Connection.RemoteIpAddress);

    /// <summary>
    /// The limit key of <paramref name="address"/>: an IPv4 address whole (also written as IPv4-mapped IPv6), an IPv6
    /// address as its /64 (<c>2001:db8:1:2::/64</c>); empty for none.
    /// </summary>
    public static string Of(IPAddress? address)
    {
        if (address is null)
            return string.Empty;
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetworkV6 || IPAddress.IsLoopback(address))
            return address.ToString();

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, NetworkBytes, bytes.Length - NetworkBytes);
        return $"{new IPAddress(bytes)}/64";
    }
}
