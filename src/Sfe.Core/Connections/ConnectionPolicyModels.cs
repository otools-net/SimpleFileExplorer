using Sfe.Core.Access;

namespace Sfe.Core.Connections;

/// <summary>
/// What a subject's folder access in a library- or server-level connection is compiled from
/// (<see cref="ConnectionPolicies.Compile"/>): its groups that use the connection, and the rules of the library.
/// </summary>
/// <param name="Groups">The subject's groups in the library of the request having a row for the connection.</param>
/// <param name="Rules">The library's rules in the connection for those groups and for the subject's account.</param>
public sealed record ConnectionPolicyInput(
    IReadOnlyList<ConnectionPolicyGroup> Groups,
    IReadOnlyList<ConnectionPolicyRule> Rules
);

/// <summary>
/// A group of the subject using a connection: its row's <paramref name="Level"/> where no rule of it applies, and
/// the library rights it grants (only at the levels it has: rights stay with the group that grants them).
/// </summary>
public sealed record ConnectionPolicyGroup(int GroupId, FolderLevel Level, IReadOnlySet<string> Rights);

/// <summary>A folder rule of a connection for a group (<paramref name="GroupId"/>) or the subject's account.</summary>
public sealed record ConnectionPolicyRule(int? GroupId, string Path, FolderLevel Level);
