using Otools.Net.Sfe.Core.Files;

namespace Otools.Net.Sfe.Core.Transfers;

/// <summary>A transfer job's run in the task history: who, what, from where to where, how it went.</summary>
internal static class TransferSummary
{
    public static string Of(TransferStart start, FileJobView view, FileJobState state)
    {
        var asked = start.Paths.Distinct(StringComparer.Ordinal).Count();
        var done = view.Results.Count(r => r.Status == FileOperationStatus.Done);
        var skipped = view.Results.Count(r => r.Status == FileOperationStatus.Skipped);
        var failed = view.Results.Count - done - skipped;
        var verb = start.Kind == FileJobKind.Move ? "moved" : "copied";
        var again = start.Retry ? " again" : "";
        var parts = new List<string> { $"{done:N0} done" };
        if (skipped > 0)
            parts.Add($"{skipped:N0} left out");
        if (failed > 0)
            parts.Add($"{failed:N0} failed");
        if (asked - view.Results.Count is > 0 and var left)
            parts.Add($"{left:N0} not reached");
        var ended = state == FileJobState.Cancelled ? " Cancelled." : "";
        return $"{start.Actor.Name} {verb}{again} {Items(asked)} from {start.SourceText} to {Target(start)}: "
            + $"{string.Join(", ", parts)}.{ended}";
    }

    /// <summary>The target folder and its place.</summary>
    private static string Target(TransferStart start) =>
        start.To.Path is { Length: > 0 } folder ? $"“{folder}” in {start.TargetText}" : start.TargetText;

    private static string Items(int count) => count == 1 ? "1 item" : $"{count:N0} items";
}
