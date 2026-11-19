namespace Otools.Net.Sfe.Client;

/// <summary>A tag and how many files of a scope have it (<see cref="SfeScopeTags"/>).</summary>
public sealed class SfeTagUsage
{
    /// <summary>The tag.</summary>
    public SfeTagSummary Tag { get; init; } = new();

    /// <summary>The files of the scope having it.</summary>
    public int FileCount { get; init; }
}
