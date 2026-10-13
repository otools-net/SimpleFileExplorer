using System.Text.RegularExpressions;

namespace Sfe.Core.Upgrades;

/// <param name="Title">The heading: Added, Changed, Fixed.</param>
/// <param name="Items">One line per change, in the changelog's Markdown (<c>`code`</c>, <c>**bold**</c>).</param>
public sealed record ChangelogSection(string Title, IReadOnlyList<string> Items);

public sealed record ChangelogEntry(string Version, IReadOnlyList<ChangelogSection> Sections);

/// <summary>
/// The versions of <c>CHANGELOG.md</c> (built into the application): <c>## x.y.z</c>, then <c>### Added</c>... and
/// <c>- </c> items, whose indented lines continue them. What the update page shows before it changes anything.
/// </summary>
public static partial class Changelog
{
    public static IReadOnlyList<ChangelogEntry> Parse(string markdown)
    {
        var entries = new List<ChangelogEntry>();
        string? version = null;
        List<ChangelogSection>? sections = null;
        string? title = null;
        List<string>? items = null;

        void EndSection()
        {
            if (sections is not null && title is not null && items is { Count: > 0 })
                sections.Add(new ChangelogSection(title, items));
            title = null;
            items = null;
        }

        void EndVersion()
        {
            EndSection();
            if (version is not null && sections is not null)
                entries.Add(new ChangelogEntry(version, sections));
            version = null;
            sections = null;
        }

        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (VersionHeading().Match(line) is { Success: true } heading)
            {
                EndVersion();
                version = heading.Groups["version"].Value;
                sections = [];
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                // Another level-2 heading (not a version) ends the current one.
                EndVersion();
            }
            else if (sections is not null && line.StartsWith("### ", StringComparison.Ordinal))
            {
                EndSection();
                title = line[4..].Trim();
                items = [];
            }
            else if (items is not null && line.StartsWith("- ", StringComparison.Ordinal))
            {
                items.Add(line[2..].Trim());
            }
            else if (items is { Count: > 0 } && line.Length > 0 && char.IsWhiteSpace(line[0]))
            {
                items[^1] += " " + line.Trim();
            }
        }

        EndVersion();
        return entries;
    }

    /// <summary>
    /// The versions after <paramref name="from"/> (null = every one: the install's version is unknown) up to
    /// <paramref name="to"/>, newest first.
    /// </summary>
    public static IReadOnlyList<ChangelogEntry> Between(IEnumerable<ChangelogEntry> entries, SemanticVersion? from, SemanticVersion to) =>
        entries
            .Select(e => (Entry: e, Version: SemanticVersion.TryParse(e.Version, out var v) ? v : null))
            .Where(e => e.Version is not null && (from is null || e.Version > from) && e.Version <= to)
            .OrderByDescending(e => e.Version)
            .Select(e => e.Entry)
            .ToList();

    [GeneratedRegex(@"^##\s+\[?v?(?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)\]?\s*$")]
    private static partial Regex VersionHeading();
}
