namespace Sfe.Core.Data;

public sealed class FileTag
{
    public int FileId { get; set; }
    public FileEntry? File { get; set; }
    public int TagId { get; set; }
    public Tag? Tag { get; set; }
}
