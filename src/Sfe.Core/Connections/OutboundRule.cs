namespace Sfe.Core.Connections;

/// <summary>
/// Where a provider's calls may go (<see cref="OutboundPolicy"/>).
/// </summary>
/// <param name="Hosts">
/// The only hosts called: a name (<c>api.example.com</c>) or every name under one (<c>*.example.com</c>). Empty for
/// a provider whose address a person types: any host, checked by <paramref name="PrivateAddresses"/>.
/// </param>
/// <param name="PrivateAddresses">
/// Addresses of this computer and its network may be called (loopback, private, link-local). Only read without
/// <paramref name="Hosts"/>: fixed hosts are the provider's own.
/// </param>
public sealed record OutboundRule(IReadOnlyList<string> Hosts, bool PrivateAddresses = false)
{
    /// <summary>The rule lets a call to <paramref name="address"/> start: https, and a host of the list.</summary>
    public bool Allows(Uri address) =>
        address.IsAbsoluteUri
        && address.Scheme == Uri.UriSchemeHttps
        && (Hosts.Count == 0 || Hosts.Any(pattern => Matches(pattern, address.IdnHost)));

    private static bool Matches(string pattern, string host) =>
        pattern.StartsWith("*.", StringComparison.Ordinal)
            ? host.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase) && host.Length > pattern.Length - 1
            : string.Equals(pattern, host, StringComparison.OrdinalIgnoreCase);
}
