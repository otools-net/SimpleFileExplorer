namespace Sfe.Core.Data;

/// <summary>Alternate name of a tag.</summary>
public sealed class TagAlias
{
    public int Id { get; set; }
    public int TagId { get; set; }
    public Tag? Tag { get; set; }
    public string Name { get; set; } = string.Empty;
}
