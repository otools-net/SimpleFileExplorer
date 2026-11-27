using System.Text;

namespace Otools.Net.Sfe.Core.Files;

/// <summary>
/// Where file systems are mounted on Linux, from <c>/proc/self/mountinfo</c>, read again at most every
/// <see cref="KeptFor"/>: the mount holding a folder is the longest mount point its real path starts with. It finds a
/// file system mounted on a folder that is not a link (a Docker bind mount, a disk mounted under the library) without
/// measuring every folder. Null elsewhere (Windows, macOS) or when the list can't be read.
/// </summary>
public static class MountPoints
{
    public static readonly TimeSpan KeptFor = TimeSpan.FromMinutes(2);

    private const string MountInfo = "/proc/self/mountinfo";

    private static readonly Lock Gate = new();
    private static (long? ReadAt, IReadOnlyList<string>? Points) _kept = (null, null);

    /// <summary>The mount point holding <paramref name="realPath"/> (links resolved); null when unknown.</summary>
    public static string? Holding(string realPath) => Holding(realPath, Current());

    /// <summary>The longest of <paramref name="points"/> that is <paramref name="realPath"/> or holds it.</summary>
    public static string? Holding(string realPath, IReadOnlyList<string>? points) =>
        points?.Where(point => IsSameOrInside(realPath, point)).MaxBy(point => point.Length);

    // Linux paths, whatever the system running the tests: '/' and case-sensitive names.
    private static bool IsSameOrInside(string path, string point) =>
        point == "/"
        || string.Equals(path.TrimEnd('/'), point, StringComparison.Ordinal)
        || path.StartsWith(point + "/", StringComparison.Ordinal);

    /// <summary>
    /// The mount points of a <c>mountinfo</c> text: the fifth field of each line, its octal escapes (<c>\040</c> for a
    /// space) decoded.
    /// </summary>
    public static IReadOnlyList<string> Parse(string mountInfo) =>
        mountInfo
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(' '))
            .Where(fields => fields.Length > 4 && fields[4].StartsWith('/'))
            .Select(fields => Unescape(fields[4]))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static IReadOnlyList<string>? Current()
    {
        if (!OperatingSystem.IsLinux())
            return null;
        lock (Gate)
        {
            if (_kept.ReadAt is { } readAt && Environment.TickCount64 - readAt < (long)KeptFor.TotalMilliseconds)
                return _kept.Points;
            _kept = (Environment.TickCount64, Read());
            return _kept.Points;
        }
    }

    private static IReadOnlyList<string>? Read()
    {
        try
        {
            return Parse(File.ReadAllText(MountInfo));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Unescape(string field)
    {
        if (!field.Contains('\\'))
            return field;
        var text = new StringBuilder(field.Length);
        for (var i = 0; i < field.Length; i++)
        {
            if (field[i] == '\\' && i + 3 < field.Length && IsOctal(field.AsSpan(i + 1, 3)))
            {
                text.Append((char)Convert.ToInt32(field.Substring(i + 1, 3), 8));
                i += 3;
            }
            else
            {
                text.Append(field[i]);
            }
        }
        return text.ToString();
    }

    private static bool IsOctal(ReadOnlySpan<char> digits) =>
        digits.Length == 3
        && digits[0] is >= '0' and <= '7'
        && digits[1] is >= '0' and <= '7'
        && digits[2] is >= '0' and <= '7';
}
