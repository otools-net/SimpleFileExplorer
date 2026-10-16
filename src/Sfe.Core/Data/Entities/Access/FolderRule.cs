using Sfe.Core.Access;

namespace Sfe.Core.Data;

/// <summary>
/// What a group of a library, or one account, may do in a folder and below it (<see cref="FolderPolicy"/>): the
/// nearest rule above a path wins for each of them. Kept by path, like quick links: a rule whose folder went away
/// stays until it is removed or re-pointed (<see cref="FolderRuleService"/>).
/// </summary>
public sealed class FolderRule
{
    public int Id { get; set; }
    public int TenantId { get; set; }

    /// <summary>Relative to the library root, '/' separated; empty for the root itself.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>The group the rule is for; null for a rule of an account (<see cref="UserId"/>).</summary>
    public int? GroupId { get; set; }

    public UserGroup? Group { get; set; }

    /// <summary>The account the rule is for; null for a rule of a group (<see cref="GroupId"/>).</summary>
    public int? UserId { get; set; }

    public FolderLevel Level { get; set; }

    public DateTime Created { get; set; }

    /// <summary>The account that set the rule; null when it is gone, or for rules made by the server itself.</summary>
    public int? CreatedById { get; set; }
}
