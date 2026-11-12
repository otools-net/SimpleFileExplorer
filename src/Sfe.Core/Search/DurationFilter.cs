using System.Globalization;

namespace Sfe.Core.Search;

/// <summary>The length of a video or audio file compared to a number of seconds (<c>d:</c>).</summary>
/// <param name="Tolerance">Seconds either side of <paramref name="Seconds"/> for
/// <see cref="SizeComparison.Approximately"/>.</param>
public sealed record DurationFilter(SizeComparison Comparison, double Seconds, double Tolerance = 0)
{
    public static bool TryParse(string value, out DurationFilter filter)
    {
        filter = null!;
        var match = SearchQuery.MatchDuration(value);
        if (!match.Success)
            return false;

        double seconds,
            unit;
        if (match.Groups["clock"].Success)
        {
            // "1:30" is a minute and a half, "1:30:00" an hour and a half, as players show them.
            seconds = match
                .Groups["clock"]
                .Value.Split(':')
                .Aggregate(0d, (total, part) => total * 60 + int.Parse(part, CultureInfo.InvariantCulture));
            unit = 1;
        }
        else
        {
            unit = match.Groups["unit"].Value.ToLowerInvariant() switch
            {
                "s" => 1,
                "h" => 3600,
                _ => 60,
            };
            seconds = SearchQuery.ParseNumber(match.Groups["num"].Value) * unit;
        }

        var comparison = match.Groups["op"].Value switch
        {
            "<" => SizeComparison.LessThan,
            "<=" => SizeComparison.LessThanOrEqual,
            ">" => SizeComparison.GreaterThan,
            ">=" => SizeComparison.GreaterThanOrEqual,
            "=" => SizeComparison.Equal,
            _ => SizeComparison.Approximately,
        };

        // "d:10m" is about ten minutes: anything that rounds to it in the unit typed.
        filter = new DurationFilter(comparison, seconds, comparison == SizeComparison.Approximately ? unit / 2 : 0);
        return true;
    }
}
