using System.Collections.Generic;

namespace Otools.Net.Sfe.Client;

/// <summary>A tag with its aliases, parents and number of files.</summary>
public sealed class SfeTag
{
    /// <summary>Its id.</summary>
    public int Id { get; init; }

    /// <summary>Its name.</summary>
    public string Name { get; init; } = "";

    /// <summary>A short name searches also find it by; null for none.</summary>
    public string? Shorthand { get; init; }

    /// <summary>Its name as shown, made unique where two tags share a name.</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>Its color's word (<c>teal</c>); null for none.</summary>
    public string? Color { get; init; }

    /// <summary>Whether it groups the tags inheriting from it (a category).</summary>
    public bool IsCategory { get; init; }

    /// <summary>Whether files having it are left out of searches that don't name it (Archived).</summary>
    public bool IsHidden { get; init; }

    /// <summary>Whether it comes with the app (it can't be deleted).</summary>
    public bool IsBuiltIn { get; init; }

    /// <summary>The tag whose name tells this one apart from another of the same name; null for none.</summary>
    public int? DisambiguationId { get; init; }

    /// <summary>Other names searches find it by.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>The tags it inherits from: a search of a parent finds its files too.</summary>
    public IReadOnlyList<SfeTagSummary> Parents { get; init; } = [];

    /// <summary>How many files the caller sees have it.</summary>
    public int FileCount { get; init; }
}
