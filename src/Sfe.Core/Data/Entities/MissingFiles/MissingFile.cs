namespace Sfe.Core.Data;

/// <summary>
/// A tagged file a scan no longer found, kept with its tags until it turns up again (moved, renamed, its drive back)
/// and is relinked, see <see cref="FileTracking.RelinkService"/>. Its own table rather than a flag on <c>Files</c>:
/// searches and folder deletes stay as they are, and an existing SQLite index can be given a new table, not new
/// columns.
/// </summary>
public sealed class MissingFile
{
    public int Id { get; set; }

    /// <summary>Absolute physical path where the file was.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>File name without extension, as in <see cref="FileEntry.Name"/>.</summary>
    public string Name { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public long Size { get; set; }
    public string? Sha1 { get; set; }
    public DateTime LastWriteTime { get; set; }

    /// <summary>When the file was first indexed: the file it moved to was indexed later.</summary>
    public DateTime RegisterTime { get; set; }

    /// <summary>When the scan noticed it was gone.</summary>
    public DateTime MissingSince { get; set; }

    public List<MissingFileTag> TagLinks { get; set; } = [];

    public override string ToString() => FullName;
}
