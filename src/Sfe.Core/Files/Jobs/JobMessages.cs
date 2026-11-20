using Otools.Net.Sfe.Core.Files.Compression;

namespace Otools.Net.Sfe.Core.Files;

/// <summary>
/// What the library's file jobs say of an item done (compressions, extractions), for the operations panel.
/// </summary>
internal static class JobMessages
{
    /// <summary>
    /// What a compression says when it is done: files and bytes in, the zip's size, what it left out.
    /// </summary>
    public static string Compressed(ArchiveSummary summary, long zipLength, bool encrypted)
    {
        var ratio = summary.Bytes > 0 ? $" ({zipLength * 100.0 / summary.Bytes:0} %)" : string.Empty;
        var text =
            $"{Count(summary.Files, "file")}: {FormatBytes(summary.Bytes)} → "
            + $"{FormatBytes(zipLength)}{ratio}{(encrypted ? ", encrypted" : string.Empty)}.";
        if (summary.LeftOut.Count > 0)
            text += $" Left out {Count(summary.LeftOut.Count, "item")} (listed in “{Archives.LeftOutEntry}”).";
        return text;
    }

    /// <summary>
    /// What an extraction says when it is done: what it wrote, what it left out and why, where it stopped.
    /// </summary>
    public static string Extracted(ExtractResult result, int skipped)
    {
        var parts = new List<string>
        {
            result.Files + result.Folders == 0
                ? "Nothing was extracted."
                : $"{Count(result.Files, "file")} ({FormatBytes(result.Bytes)}).",
        };
        if (result.LeftOutCount > 0)
        {
            var named = string.Join("; ", result.LeftOut.Take(5).Select(l => $"“{l.Name}”: {l.Reason}"));
            parts.Add(
                $"Left out {Count(result.LeftOutCount, "entry", "entries")}: "
                    + $"{named}{(result.LeftOutCount > 5 ? $"; and {result.LeftOutCount - 5:N0} more" : string.Empty)}."
            );
        }
        if (skipped > 0)
            parts.Add($"{Count(skipped, "item")} not placed: there is one of that name in the folder.");
        if (result.Stopped is { } stopped)
            parts.Add(stopped);
        return string.Join(' ', parts);
    }

    private static string Count(long count, string one, string? many = null) =>
        count == 1 ? $"1 {one}" : $"{count:N0} {many ?? one + "s"}";

    private static string FormatBytes(long bytes) =>
        bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
            < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.#} MB",
            _ => $"{bytes / 1024.0 / 1024 / 1024:0.##} GB",
        };

    public static string Lower(string message) =>
        message.Length > 0 ? char.ToLowerInvariant(message[0]) + message[1..] : message;
}
