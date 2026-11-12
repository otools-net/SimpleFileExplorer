namespace Sfe.Core.Logs;

/// <param name="Entries">Newest first.</param>
/// <param name="Next">The <c>before</c> of the next (older) page; null on the last one.</param>
public sealed record LogPage(IReadOnlyList<LogEntryView> Entries, long? Next);
