namespace Sfe.Core.Trickplay;

/// <summary>Detailed progress of the background job, for the activity dialog.</summary>
/// <param name="Queued">The first videos opened in the player and waiting (they go first); the total is in the
/// status.</param>
/// <param name="Pending">The first videos the pass will process next; the total is in the status.</param>
/// <param name="Recent">The last videos finished, the most recent first.</param>
public sealed record TrickplayActivity(
    TrickplayStatus Status,
    IReadOnlyList<TrickplayRunning> Running,
    IReadOnlyList<string> Queued,
    IReadOnlyList<string> Pending,
    IReadOnlyList<TrickplayResult> Recent
);
