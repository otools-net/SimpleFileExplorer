namespace Otools.Net.Sfe.Core.Connections.Previews;

/// <summary>Why the connections' cache doesn't take a file (<see cref="ConnectionCacheException"/>).</summary>
public enum CacheRefusal
{
    /// <summary>Its size limit is 0, or its folder is one the app serves.</summary>
    Off,

    /// <summary>Larger than <see cref="Options.ConnectionCacheOptions.MaxFileMegabytes"/>.</summary>
    TooLarge,

    /// <summary>The caller downloads as many files as it may at once, or the server does.</summary>
    Busy,

    /// <summary>The provider has no content for it (an online document), or it changed while read.</summary>
    NoContent,
}
