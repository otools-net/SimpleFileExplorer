using System.Diagnostics.CodeAnalysis;

namespace Sfe.Core.Upgrades;

/// <summary>
/// <c>major.minor.patch</c> with an optional pre-release (<c>1.3.0-beta.1</c>), as the builds name SFE's versions:
/// a pre-release comes before its release; build metadata (<c>+sha</c>) is ignored.
/// </summary>
public sealed record SemanticVersion(int Major, int Minor, int Patch, string? PreRelease = null) : IComparable<SemanticVersion>
{
    public static bool TryParse(string? text, [NotNullWhen(true)] out SemanticVersion? version)
    {
        version = null;
        var core = text?.Trim().Split('+')[0];
        if (string.IsNullOrEmpty(core))
            return false;

        var dash = core.IndexOf('-');
        var numbers = (dash < 0 ? core : core[..dash]).Split('.');
        var preRelease = dash < 0 ? null : core[(dash + 1)..];
        if (numbers.Length != 3 || preRelease is "" || !int.TryParse(numbers[0], out var major) || !int.TryParse(numbers[1], out var minor)
            || !int.TryParse(numbers[2], out var patch) || major < 0 || minor < 0 || patch < 0)
            return false;

        version = new SemanticVersion(major, minor, patch, preRelease);
        return true;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
            return 1;
        var result = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (result != 0)
            return result;
        return (PreRelease, other.PreRelease) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            var (a, b) => string.CompareOrdinal(a, b),
        };
    }

    public static bool operator <(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) >= 0;

    public override string ToString() => PreRelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{PreRelease}";
}
