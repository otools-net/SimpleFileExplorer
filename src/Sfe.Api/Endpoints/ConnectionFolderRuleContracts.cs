using Sfe.Core.Access;
using Sfe.Core.Connections;

namespace Sfe.Api.Endpoints;

/// <summary>A folder rule of a connection, as the library's rules are listed (<see cref="FolderRuleView"/>).</summary>
/// <param name="Path">Folder, relative to the connection's root folder (empty: the root).</param>
/// <param name="Name">The group's or the account's name.</param>
/// <param name="ItemId">The provider's id of the folder, when known.</param>
public sealed record ConnectionFolderRuleView(
    int Id,
    string Path,
    int? GroupId,
    int? UserId,
    string Name,
    FolderLevel Level,
    string? ItemId
);

/// <summary>Every folder rule of the request's library in a connection: those not listed are removed.</summary>
public sealed record ConnectionFolderRulesInput(IReadOnlyList<ConnectionRuleInput>? Rules);
