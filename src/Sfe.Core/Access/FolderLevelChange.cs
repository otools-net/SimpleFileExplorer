namespace Sfe.Core.Access;

/// <summary>
/// How a principal's level at a folder changes when the folder moves. Null for an account without a rule above the
/// place: it has no say there (its groups decide).
/// </summary>
public sealed record FolderLevelChange(FolderPrincipalRef Principal, FolderLevel? Before, FolderLevel? After)
{
    /// <summary>
    /// The principal may do more than before at the folder (sees it, or may change its files). An account at Hidden
    /// adds nothing to its groups, as one without a say.
    /// </summary>
    public bool Gains => (After ?? FolderLevel.Hidden) > (Before ?? FolderLevel.Hidden);
}
