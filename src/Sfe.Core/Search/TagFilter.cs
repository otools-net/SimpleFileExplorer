namespace Otools.Net.Sfe.Core.Search;

/// <param name="Name">Tag name, shorthand or alias; null means "any tag".</param>
/// <param name="Negated">Files must not have the tag.</param>
public sealed record TagFilter(string? Name, bool Negated = false);
