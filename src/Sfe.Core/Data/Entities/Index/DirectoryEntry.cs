namespace Sfe.Core.Data;

public sealed class DirectoryEntry : IndexEntry
{
    public int? ParentId { get; set; }
    public DirectoryEntry? Parent { get; set; }
    public List<DirectoryEntry> Directories { get; set; } = [];
    public List<FileEntry> Files { get; set; } = [];
}
