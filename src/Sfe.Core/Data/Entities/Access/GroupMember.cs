namespace Sfe.Core.Data;

public sealed class GroupMember
{
    public int GroupId { get; set; }
    public UserGroup? Group { get; set; }
    public int UserId { get; set; }
    public UserAccount? User { get; set; }
}
