namespace Sfe.Core.Data;

/// <summary>A tag of a file in the trash, given back by a restore.</summary>
public sealed class TrashTag
{
    public int Id { get; set; }
    public int TrashEntryId { get; set; }
    public TrashEntry? Entry { get; set; }

    /// <summary>The file, relative to the trashed item ('/' separated); empty for the trashed file itself.</summary>
    public string Path { get; set; } = string.Empty;

    public int TagId { get; set; }
    public Tag? Tag { get; set; }
}
