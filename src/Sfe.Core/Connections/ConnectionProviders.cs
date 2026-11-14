using Otools.Net.Sfe.Core.Options;

namespace Otools.Net.Sfe.Core.Connections;

/// <summary>The kinds of storage this server knows, by <see cref="IConnectionProvider.Kind"/>.</summary>
public sealed class ConnectionProviders(IEnumerable<IConnectionProvider> providers)
{
    private readonly Dictionary<string, IConnectionProvider> _byKind = providers.ToDictionary(
        p => p.Kind,
        StringComparer.OrdinalIgnoreCase
    );

    public IReadOnlyCollection<IConnectionProvider> All => _byKind.Values;

    public IConnectionProvider? Find(string? kind) => kind is null ? null : _byKind.GetValueOrDefault(kind);

    /// <summary>
    /// The kinds a connection can be made of now: connections on, the kind allowed, and configured enough to sign in.
    /// </summary>
    public IReadOnlyList<IConnectionProvider> Offered(ConnectionOptions options) =>
        [.. _byKind.Values.Where(p => IsAllowed(p.Kind, options) && p.SignInMethods(options).Count > 0)];

    /// <summary>
    /// The settings let connections of <paramref name="kind"/> work (those made before a kind was taken out stay,
    /// unusable).
    /// </summary>
    public static bool IsAllowed(string kind, ConnectionOptions options) =>
        options.Enabled
        && (options.Providers.Count == 0 || options.Providers.Contains(kind, StringComparer.OrdinalIgnoreCase));
}
