using System.Collections.Generic;

namespace Otools.Net.Sfe.Client;

/// <summary>A file's tags under one category tag, or those under none.</summary>
public sealed class SfeTagGroup
{
    /// <summary>The category; null for the tags under no category.</summary>
    public SfeTagSummary? Category { get; init; }

    /// <summary>The file's tags in it.</summary>
    public IReadOnlyList<SfeTagSummary> Tags { get; init; } = [];
}
