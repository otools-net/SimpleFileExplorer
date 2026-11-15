namespace Otools.Net.Sfe.Client;

/// <summary>A tag as files and listings show it.</summary>
public sealed class SfeTagSummary
{
    /// <summary>Its id.</summary>
    public int Id { get; init; }

    /// <summary>Its name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Its name as shown, made unique where two tags share a name.</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>Its color's word (<c>teal</c>); null for none.</summary>
    public string? Color { get; init; }

    /// <summary>Whether it groups the tags inheriting from it (a category).</summary>
    public bool IsCategory { get; init; }

    /// <summary>Whether files having it are left out of searches that don't name it (Archived).</summary>
    public bool IsHidden { get; init; }
}
