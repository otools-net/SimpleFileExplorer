using System.Collections.Generic;

namespace Otools.Net.Sfe.Client;

/// <summary>
/// What a tag is made or changed with (<see cref="SfeTags.CreateAsync"/>, <see cref="SfeTags.UpdateAsync"/>).
/// </summary>
public sealed class SfeTagDraft
{
    /// <summary>Its name (required).</summary>
    public string Name { get; set; } = "";

    /// <summary>A short name searches also find it by; null for none.</summary>
    public string? Shorthand { get; set; }

    /// <summary>A color's word of the server (<c>teal</c>); null for none.</summary>
    public string? Color { get; set; }

    /// <summary>Whether it groups the tags inheriting from it (a category).</summary>
    public bool IsCategory { get; set; }

    /// <summary>Whether files having it are left out of searches that don't name it.</summary>
    public bool IsHidden { get; set; }

    /// <summary>The tag whose name tells this one apart from another of the same name; null for none.</summary>
    public int? DisambiguationId { get; set; }

    /// <summary>Other names searches find it by.</summary>
    public IList<string> Aliases { get; set; } = new List<string>();

    /// <summary>The ids of the tags it inherits from.</summary>
    public IList<int> ParentIds { get; set; } = new List<int>();
}
