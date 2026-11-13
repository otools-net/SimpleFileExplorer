using System.Net;
using System.Net.Sockets;

namespace Sfe.Core.Connections;

/// <summary>
/// The one door the server's calls to providers go through: every provider gets its <see cref="HttpClient"/> here,
/// bound to an <see cref="OutboundRule"/>. https only; a host outside the rule is refused before any connection;
/// redirects are never followed by themselves (a provider following one asks again, and is checked again); for
/// addresses typed by people, the address a name resolves to is checked when connecting, so a name pointing at the
/// server's own network is refused too (agents/docs/development/security.md).
/// </summary>
public sealed class OutboundPolicy(Func<HttpMessageHandler>? transport = null)
{
    /// <summary>
    /// How long a connection to a provider may take to open: an unreachable one never holds a request.
    /// </summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long a provider may take to start answering. Reading the content it then sends is bounded by the
    /// caller's cancellation, not by this.
    /// </summary>
    public static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(60);

    /// <summary>A client whose every request is checked against <paramref name="rule"/>.</summary>
    public HttpClient CreateClient(OutboundRule rule) =>
        new(new OutboundGuard(rule, transport?.Invoke() ?? Transport(rule)), disposeHandler: true)
        {
            // Downloads last as long as they last: the guard bounds the wait for the answer's headers instead.
            Timeout = Timeout.InfiniteTimeSpan,
        };

    /// <summary>
    /// An address the server may call for a person who typed a host name: not this computer, not its network.
    /// </summary>
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return false;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // Unique local fc00::/7, link-local fe80::/10, site-local, multicast.
            return !(
                address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6UniqueLocal
                || address.IsIPv6Multicast
            );
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] switch
        {
            0 or 10 or 127 => false,
            100 => bytes[1] is < 64 or > 127, // carrier-grade NAT 100.64.0.0/10
            169 => bytes[1] != 254, // link-local, cloud metadata
            172 => bytes[1] is < 16 or > 31,
            192 => bytes[1] != 168,
            >= 224 => false, // multicast, reserved, broadcast
            _ => true,
        };
    }

    private static SocketsHttpHandler Transport(OutboundRule rule)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = ConnectTimeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        };
        if (rule.Hosts.Count == 0 && !rule.PrivateAddresses)
        {
            // A proxy would connect for us, out of sight of the check below.
            handler.UseProxy = false;
            handler.ConnectCallback = ConnectToPublicAddressAsync;
        }
        return handler;
    }

    /// <summary>
    /// Connects to the first public address of the host; the check is on the address connected to, so a name
    /// answering differently a second time (DNS rebinding) gains nothing.
    /// </summary>
    private static async ValueTask<Stream> ConnectToPublicAddressAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken
    )
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        var address = addresses.FirstOrDefault(IsPublic)
            ?? throw new RemoteStorageException(
                RemoteErrorKind.Refused,
                "The address is on this computer or its network: the server does not call those."
            );
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
