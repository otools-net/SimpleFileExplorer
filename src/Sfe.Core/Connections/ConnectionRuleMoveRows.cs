namespace Sfe.Core.Connections;

/// <summary>
/// The rows a folder move of a connection changed when it began (<see cref="ConnectionRuleMove"/>).
/// </summary>
/// <param name="Originals">Rules at the old paths, removed once the folder moved.</param>
/// <param name="AtTarget">Rules now at the new paths: the copies and pins, or the rows renamed in place.</param>
/// <param name="Stale">Other libraries' rules left where the folder goes (their folder is gone): removed once the
/// folder moved, since they would apply to it.</param>
/// <param name="PreviousPaths">A case-only rename: the paths the rows had, written back by an abort.</param>
/// <param name="OtherLibraries">The libraries other than the request's whose rules follow or go.</param>
internal sealed record ConnectionRuleMoveRows(
    ConnectionMovePlan Plan,
    IReadOnlyList<int> Originals,
    IReadOnlyList<int> AtTarget,
    IReadOnlyList<int> Stale,
    IReadOnlyDictionary<int, string>? PreviousPaths,
    IReadOnlyList<int> OtherLibraries
);
