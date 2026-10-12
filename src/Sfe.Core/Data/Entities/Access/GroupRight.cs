namespace Sfe.Core.Data;

/// <summary>A right granted to a group, by its id in <see cref="Access.RightCatalog"/>.</summary>
public sealed class GroupRight
{
    public int GroupId { get; set; }
    public UserGroup? Group { get; set; }
    public string Right { get; set; } = string.Empty;
}
