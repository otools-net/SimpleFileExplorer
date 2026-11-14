using Sfe.Core.Access;

namespace Sfe.Core.Connections;

/// <summary>
/// A folder rule of a connection, for one library's group (<paramref name="GroupId"/>) or account
/// (<paramref name="UserId"/>): <paramref name="Level"/> at <paramref name="Path"/> (relative to the connection's
/// root, <see cref="RemotePaths"/>) and below it.
/// </summary>
/// <param name="ItemId">The provider's id of the folder, when known.</param>
public sealed record ConnectionRuleInfo(
    int Id,
    string Path,
    int? GroupId,
    int? UserId,
    FolderLevel Level,
    string? ItemId
);

/// <summary>A folder rule asked for (<see cref="ConnectionRuleService.SetAsync"/>): a group or an account.</summary>
public sealed record ConnectionRuleInput(string? Path, int? GroupId, int? UserId, FolderLevel Level, string? ItemId);
