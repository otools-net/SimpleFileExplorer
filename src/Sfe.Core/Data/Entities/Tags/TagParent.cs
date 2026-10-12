namespace Sfe.Core.Data;

/// <summary>The child tag "is a" parent tag: tagging a file with the child implies the parent.</summary>
public sealed class TagParent
{
    public int ParentId { get; set; }
    public Tag? Parent { get; set; }
    public int ChildId { get; set; }
    public Tag? Child { get; set; }
}
