using System.Globalization;

namespace Sfe.Core.Library;

/// <summary>Case-insensitive comparer that orders embedded numbers numerically ("Episode 2" before "Episode 10").</summary>
public sealed class NaturalComparer : IComparer<string?>
{
    public static NaturalComparer Instance { get; } = new();

    private static readonly CompareInfo CompareInfo = CultureInfo.InvariantCulture.CompareInfo;

    public int Compare(string? x, string? y) =>
        CompareInfo.Compare(x, y, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);
}
