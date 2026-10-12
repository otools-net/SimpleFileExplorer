namespace Sfe.Core.Data;

/// <summary>
/// A tag, modelled on TagStudio's: names need not be unique, a tag can go by a shorthand and aliases, and it
/// inherits from parent tags, so a file tagged "Shrek" is also found by searching for its parent "Character".
/// </summary>
public sealed class Tag
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Short name, searchable like the name; used to disambiguate child tags.</summary>
    public string? Shorthand { get; set; }

    /// <summary>Slug of a <see cref="Tagging.TagColors"/> entry; null for the uncolored default.</summary>
    public string? Color { get; set; }

    /// <summary>Tags inheriting from a category tag are grouped under it when a file's tags are listed.</summary>
    public bool IsCategory { get; set; }

    /// <summary>Files with this tag are left out of search results unless the search asks for the tag.</summary>
    public bool IsHidden { get; set; }

    /// <summary>
    /// Parent whose shorthand (or name) is shown in parentheses after the name to tell same-named tags apart:
    /// "Freddy (FNAF)". Always one of the parents.
    /// </summary>
    public int? DisambiguationId { get; set; }
    public Tag? Disambiguation { get; set; }

    public List<TagAlias> Aliases { get; set; } = [];
    public List<TagParent> ParentLinks { get; set; } = [];
    public List<TagParent> ChildLinks { get; set; } = [];
    public List<FileTag> FileLinks { get; set; } = [];
    public TagActivity? Activity { get; set; }

    public override string ToString() => Name;
}
