namespace Sfe.Core.Data;

public sealed class MissingFileTag
{
    public int MissingFileId { get; set; }
    public MissingFile? MissingFile { get; set; }
    public int TagId { get; set; }
    public Tag? Tag { get; set; }
}
