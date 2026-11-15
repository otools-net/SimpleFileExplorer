using System.Globalization;

namespace Otools.Net.Sfe.Core.Library;

/// <summary>
/// Case-insensitive comparer that orders embedded numbers numerically ("Episode 2" before "Episode 10").
/// </summary>
public sealed class NaturalComparer : IComparer<string?>
{
    /// <summary>Longest key <see cref="KeyOf"/> gives, in characters (two per byte of the sort key).</summary>
    public const int MaxKeyLength = 400;

    private const CompareOptions Options = CompareOptions.IgnoreCase | CompareOptions.NumericOrdering;

    public static NaturalComparer Instance { get; } = new();

    private static readonly CompareInfo CompareInfo = CultureInfo.InvariantCulture.CompareInfo;

    public int Compare(string? x, string? y) => CompareInfo.Compare(x, y, Options);

    /// <summary>
    /// A text whose ordinal order is this comparer's order: the culture's sort key of <paramref name="name"/> in hex
    /// (bytes compared one by one are compared as two hex digits each), so that a database sorts names as listings
    /// do. Cut at <see cref="MaxKeyLength"/>: names alike that far tie, and the caller breaks the tie.
    /// </summary>
    public static string KeyOf(string name)
    {
        byte[] bytes;
        try
        {
            bytes = CompareInfo.GetSortKey(name, Options).KeyData;
        }
        catch (Exception error) when (error is ArgumentException or PlatformNotSupportedException)
        {
            // Without culture data (invariant globalization) there is no such key: the upper-cased name, in order.
            bytes = System.Text.Encoding.BigEndianUnicode.GetBytes(name.ToUpperInvariant());
        }
        var hex = Convert.ToHexStringLower(bytes);
        return hex.Length <= MaxKeyLength ? hex : hex[..MaxKeyLength];
    }
}
