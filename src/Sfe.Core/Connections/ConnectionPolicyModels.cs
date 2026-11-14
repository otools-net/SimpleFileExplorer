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
/// <param name="ItemId">The provider's id of the folder, when known: the rule also applies where the item is
/// now.</param>
public sealed record ConnectionPolicyRule(int? GroupId, string Path, FolderLevel Level, string? ItemId = null);

/// <summary>
/// A subject's folder access in a connection (<see cref="ConnectionPolicies.GetAsync"/>): the policy by path, and the
/// paths of its rules by the provider's id of their folder (<see cref="ConnectionFolders.At(string, string?)"/>).
/// </summary>
public sealed record ConnectionPolicy(
    FolderPolicy Folders,
    IReadOnlyDictionary<string, IReadOnlyList<string>> RulePlaces
)
{
    /// <summary>A policy without rules naming an item.</summary>
    public static ConnectionPolicy Of(FolderPolicy folders) =>
        new(folders, new Dictionary<string, IReadOnlyList<string>>());

    /// <summary>The paths of <paramref name="rules"/> by the id of their folder (those with one).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> PlacesOf(
        IEnumerable<ConnectionPolicyRule> rules
    ) =>
        rules
            .Where(r => r.ItemId is not null)
            .GroupBy(r => r.ItemId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)[.. g.Select(r => r.Path).Distinct()]);
}
