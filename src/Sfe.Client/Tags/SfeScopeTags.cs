using System.Collections.Generic;

namespace Otools.Net.Sfe.Client;

/// <summary>The tags of the files of a scope (<see cref="SfeTagScope"/>), with how many files have each.</summary>
public sealed class SfeScopeTags
{
    /// <summary>The files of the scope the caller sees.</summary>
    public int Files { get; init; }

    /// <summary>The tags those files have.</summary>
    public IReadOnlyList<SfeTagUsage> Tags { get; init; } = [];
}
