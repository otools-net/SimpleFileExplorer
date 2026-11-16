namespace Otools.Net.Sfe.Core.Connections.Previews;

/// <summary>The connections' cache doesn't take a file now (<see cref="Reason"/>).</summary>
public sealed class ConnectionCacheException(CacheRefusal reason)
    : Exception($"The connections' cache doesn't take the file: {reason}.")
{
    public CacheRefusal Reason { get; } = reason;
}
