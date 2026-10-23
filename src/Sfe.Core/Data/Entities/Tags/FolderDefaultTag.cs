namespace Sfe.Core.Data;

/// <summary>
/// A tag every file entering a folder (or one of its sub-folders) gets: indexed by a scan, created, uploaded, copied or
/// moved there (agents/docs/features/default-tags.md). Kept by path, as task schedules are: the folder needn't be indexed.
/// </summary>
public sealed class FolderDefaultTag
{
    public int Id { get; set; }

    /// <summary>The folder, relative to the library root ('/' separated, canonical); empty for the root.</summary>
    public string Path { get; set; } = string.Empty;

    public int TagId { get; set; }
    public Tag? Tag { get; set; }

    /// <summary>When it was set (UTC).</summary>
    public DateTime Created { get; set; }
}
