using System.Collections.Generic;

namespace Otools.Net.Sfe.Client;

/// <summary>The tags of a file.</summary>
public sealed class SfeFileTags
{
    /// <summary>The file's path.</summary>
    public string Path { get; init; } = "";

    /// <summary>Its tags.</summary>
    public IReadOnlyList<SfeTagSummary> Tags { get; init; } = [];

    /// <summary>The same tags grouped by category tag, as the app shows them.</summary>
    public IReadOnlyList<SfeTagGroup> Groups { get; init; } = [];
}
