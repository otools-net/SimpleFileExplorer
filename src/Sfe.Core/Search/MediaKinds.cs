namespace Sfe.Core.Search;

/// <summary>The extensions (without dot) the listing classifies as each kind of media (<c>type:</c>).</summary>
public sealed record MediaKinds(
    IReadOnlyCollection<string> Images,
    IReadOnlyCollection<string> Videos,
    IReadOnlyCollection<string> Audio
);
