using Otools.Net.Sfe.Core.Data;

namespace Otools.Net.Sfe.Core.Access;

/// <param name="ExternalId">The directory's own id of the account (for local accounts, the row's id).</param>
/// <param name="Groups">The directory groups it belongs to, matched to <see cref="UserGroup.ExternalName"/>.</param>
public sealed record DirectoryUser(
    string Source,
    string ExternalId,
    string UserName,
    string? DisplayName,
    IReadOnlyList<string> Groups
);
