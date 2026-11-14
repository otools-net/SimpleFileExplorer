namespace Otools.Net.Sfe.Core.Upgrades;

public sealed record ChangelogEntry(string Version, IReadOnlyList<ChangelogSection> Sections);
