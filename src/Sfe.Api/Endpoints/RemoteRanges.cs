using System.Net.Http.Headers;
using Sfe.Core.Connections;

namespace Sfe.Api.Endpoints;

/// <summary>
/// The <c>Range</c> header of a read of a connection's file, as one <see cref="RemoteRange"/>. Only one range is
/// served (players ask for one); several, or a header that doesn't parse, read the whole file, as HTTP allows.
/// </summary>
public static class RemoteRanges
{
    /// <summary>What to read: null = the whole file.</summary>
    /// <param name="size">The file's size; null when the provider gives none (the whole file is read).</param>
    /// <param name="unsatisfiable">The range starts past the end: the answer is 416.</param>
    public static RemoteRange? Parse(string? header, long? size, out bool unsatisfiable)
    {
        unsatisfiable = false;
        if (
            string.IsNullOrEmpty(header)
            || size is not { } length
            || !RangeHeaderValue.TryParse(header, out var parsed)
            || !string.Equals(parsed.Unit, "bytes", StringComparison.OrdinalIgnoreCase)
            || parsed.Ranges.Count != 1
        )
        {
            return null;
        }

        var range = parsed.Ranges.Single();
        if (range.From is null)
        {
            // The last n bytes.
            if (range.To is not { } suffix || suffix == 0 || length == 0)
                return Unsatisfiable(out unsatisfiable);
            return new RemoteRange(Math.Max(0, length - suffix), length - 1);
        }
        if (range.From >= length)
            return Unsatisfiable(out unsatisfiable);
        var to = range.To is { } end ? Math.Min(end, length - 1) : length - 1;
        return new RemoteRange(range.From.Value, to);
    }

    private static RemoteRange? Unsatisfiable(out bool unsatisfiable)
    {
        unsatisfiable = true;
        return null;
    }
}
