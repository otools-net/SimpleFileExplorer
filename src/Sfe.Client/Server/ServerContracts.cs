using System.Collections.Generic;

namespace Otools.Net.Sfe.Client.Server;

// What the facade reads of GET api/v1/config; the rest of it is ignored.

/// <summary>The answer of <c>GET api/v1/config</c>, the parts the facade uses.</summary>
internal sealed class ConfigAnswer
{
    public string Version { get; init; } = "";
    public string? Commit { get; init; }
    public SfeServerMode Mode { get; init; }
    public List<string> Rights { get; init; } = [];
    public SfeLibrary? Tenant { get; init; }
    public List<SfeLibrary> Tenants { get; init; } = [];
    public ConfigFeatures? Features { get; init; }
    public object? Connections { get; init; }
    public ConfigTokens? AccessTokens { get; init; }
}

/// <summary>The <c>features</c> of the configuration.</summary>
internal sealed class ConfigFeatures
{
    public bool Thumbnails { get; init; }
    public bool Trickplay { get; init; }
}

/// <summary>The <c>accessTokens</c> of the configuration.</summary>
internal sealed class ConfigTokens
{
    public bool Enabled { get; init; }
}
