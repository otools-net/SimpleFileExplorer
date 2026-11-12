namespace Sfe.Core.Tagging;

/// <summary>A default tag a folder gets from one of its parents.</summary>
/// <param name="Path">The parent folder it is set on (root-relative; empty for the root).</param>
public sealed record InheritedDefaultTag(string Path, TagSummary Tag);
