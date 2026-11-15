using System;

namespace Otools.Net.Sfe.Client;

/// <summary>One of the two items of a name clash.</summary>
public sealed class SfeClashSide
{
    /// <summary>Its name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Whether it is a folder.</summary>
    public bool IsFolder { get; init; }

    /// <summary>Its size in bytes; null for a folder.</summary>
    public long? Size { get; init; }

    /// <summary>When it was last changed.</summary>
    public DateTimeOffset? Modified { get; init; }
}
