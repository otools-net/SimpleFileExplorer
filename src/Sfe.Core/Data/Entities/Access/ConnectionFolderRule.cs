using Sfe.Core.Access;
using Sfe.Core.Connections;

namespace Sfe.Core.Data;

/// <summary>
/// What a group of a library, or one account, may do in a folder of a library- or server-level
/// <see cref="Connection"/> and below it: the connection's own folder rules, set by each library for its people
/// (<see cref="ConnectionRuleService"/>). Never on an account-level connection.
/// </summary>
public sealed class ConnectionFolderRule
{
    /// <summary>Longest provider id of a folder kept with a rule.</summary>
    public const int MaxItemIdLength = 200;

    public int Id { get; set; }
    public int ConnectionId { get; set; }
    public Connection? Connection { get; set; }

    /// <summary>The library whose people the rule is for: each library sets its own in a server connection.</summary>
    public int TenantId { get; set; }

    /// <summary>Relative to the connection's root folder (<see cref="RemotePaths"/>); empty for the root.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>The group the rule is for; null for a rule of an account (<see cref="UserId"/>).</summary>
    public int? GroupId { get; set; }

    public UserGroup? Group { get; set; }

    /// <summary>The account the rule is for; null for a rule of a group (<see cref="GroupId"/>).</summary>
    public int? UserId { get; set; }

    public FolderLevel Level { get; set; }

    /// <summary>The provider's id of the folder, when it has one: the folder is found again after a move.</summary>
    public string? ItemId { get; set; }

    public DateTime Created { get; set; }

    /// <summary>The account that set the rule; null when it is gone.</summary>
    public int? CreatedById { get; set; }
}
