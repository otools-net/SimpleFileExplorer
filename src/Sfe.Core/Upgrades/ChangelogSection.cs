namespace Otools.Net.Sfe.Core.Upgrades;

/// <param name="Title">The heading: Added, Changed, Fixed.</param>
/// <param name="Items">One line per change, in the changelog's Markdown (<c>`code`</c>, <c>**bold**</c>).</param>
public sealed record ChangelogSection(string Title, IReadOnlyList<string> Items);
