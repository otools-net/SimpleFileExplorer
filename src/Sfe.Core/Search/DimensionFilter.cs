using System.Globalization;

namespace Sfe.Core.Search;

/// <summary>A side of an image or video compared to a number of pixels (<c>w:</c>, <c>h:</c>, <c>r:</c>).</summary>
public sealed record DimensionFilter(Dimension Dimension, SizeComparison Comparison, int Pixels)
{
    /// <summary>
    /// The filters of a <c>w:</c>, <c>h:</c> or <c>r:</c> token: none when malformed, two for <c>r:</c>.
    /// </summary>
    public static IReadOnlyList<DimensionFilter> Parse(string token)
    {
        var prefix = token[..2].ToLowerInvariant();
        var value = token[2..];
        if (prefix == SearchQuery.ResolutionPrefix)
        {
            var match = SearchQuery.MatchResolution(value);
            if (
                !match.Success
                || !TryPixels(match.Groups["width"].Value, out var width)
                || !TryPixels(match.Groups["height"].Value, out var height)
            )
            {
                return [];
            }

            var comparison = ToComparison(match.Groups["op"].Value);
            return [new(Dimension.Width, comparison, width), new(Dimension.Height, comparison, height)];
        }

        var side = SearchQuery.MatchPixels(value);
        return side.Success && TryPixels(side.Groups["num"].Value, out var pixels)
            ?
            [
                new(
                    prefix == SearchQuery.WidthPrefix ? Dimension.Width : Dimension.Height,
                    ToComparison(side.Groups["op"].Value),
                    pixels
                ),
            ]
            : [];
    }

    private static bool TryPixels(string value, out int pixels) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out pixels);

    /// <summary>Without an operator, a number of pixels is exact: there is no "about" as for sizes.</summary>
    private static SizeComparison ToComparison(string op) =>
        op switch
        {
            "<" => SizeComparison.LessThan,
            "<=" => SizeComparison.LessThanOrEqual,
            ">" => SizeComparison.GreaterThan,
            ">=" => SizeComparison.GreaterThanOrEqual,
            _ => SizeComparison.Equal,
        };
}
