namespace Otools.Net.Sfe.Core.Search;

public sealed record SizeFilter(SizeComparison Comparison, long Bytes, long Tolerance = 0)
{
    private const long Kb = 1024;

    public static bool TryParse(string value, out SizeFilter filter)
    {
        filter = null!;
        var match = SearchQuery.MatchSize(value);
        if (!match.Success)
            return false;

        var unit = match.Groups["unit"].Value.ToLowerInvariant() switch
        {
            "b" => 1L,
            "kb" => Kb,
            "gb" => Kb * Kb * Kb,
            "tb" => Kb * Kb * Kb * Kb,
            _ => Kb * Kb,
        };
        var bytes = (long)Math.Round(SearchQuery.ParseNumber(match.Groups["num"].Value) * unit);
        var comparison = match.Groups["op"].Value switch
        {
            "<" => SizeComparison.LessThan,
            "<=" => SizeComparison.LessThanOrEqual,
            ">" => SizeComparison.GreaterThan,
            ">=" => SizeComparison.GreaterThanOrEqual,
            "=" => SizeComparison.Equal,
            _ => SizeComparison.Approximately,
        };

        // "s:700mb" means "about 700 MB": anything that rounds to it in the requested unit.
        filter = new SizeFilter(comparison, bytes, comparison == SizeComparison.Approximately ? unit / 2 : 0);
        return true;
    }
}
