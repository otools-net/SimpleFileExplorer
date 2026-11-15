using System.Collections.Generic;

namespace Otools.Net.Sfe.Client;

/// <summary>The tags a text finds (<see cref="SfeTags.FindAsync"/>).</summary>
public sealed class SfeTagSearch
{
    /// <summary>The tags whose name, shorthand or alias holds the text, those starting with it first.</summary>
    public IReadOnlyList<SfeTag> Matches { get; init; } = [];

    /// <summary>The tags inheriting from a match (a search of the match finds their files too).</summary>
    public IReadOnlyList<SfeTag> Descendants { get; init; } = [];
}
