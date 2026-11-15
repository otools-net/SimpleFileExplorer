using System.Globalization;
using Otools.Net.Sfe.Core.Library;

namespace Otools.Net.Sfe.Core.Search;

/// <summary>
/// Where a page of search results goes on from: the last row of the page before, as the <c>after</c> text of the API
/// gives it ("{value}/{path}", or the bare path of the path sort).
/// </summary>
/// <param name="FullName">The key of the last file of the previous page.</param>
/// <param name="Text">Its name or extension (name and type sorts).</param>
/// <param name="Number">Its size, modification ticks or pixels (size, date and resolution sorts).</param>
/// <param name="Seconds">Its duration (duration sort).</param>
/// <param name="Unknown">Its duration, resolution or sort name is not known: the page goes on with the files without
/// one.</param>
public sealed record SearchCursor(
    string FullName,
    string? Text = null,
    long Number = 0,
    double Seconds = 0,
    bool Unknown = false
)
{
    /// <summary>
    /// Reads "{value}/{path}", or the bare path of the path sort. The value never holds a '/': names, name keys and
    /// extensions are parts of a file name, numbers have none. An empty duration or resolution stands for an unknown
    /// one, and so does an empty name where names may be missing (<paramref name="nameMayBeMissing"/>).
    /// </summary>
    /// <param name="keyOf">The key of a path of the cursor; null when the path is not valid.</param>
    /// <returns>null when the text is not a cursor of this sort.</returns>
    public static SearchCursor? Parse(string after, SortField sort, Func<string, string?> keyOf, bool nameMayBeMissing)
    {
        var path = after;
        var value = string.Empty;
        if (sort is not SortField.Path)
        {
            var slash = after.IndexOf('/');
            if (slash < 0)
                return null;
            value = after[..slash];
            path = after[(slash + 1)..];
        }

        if (keyOf(path) is not { } fullName)
            return null;
        if (value.Length == 0 && (sort is SortField.Duration or SortField.Resolution || IsName(sort, nameMayBeMissing)))
            return new SearchCursor(fullName, Unknown: true);
        return sort switch
        {
            SortField.Duration => double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var seconds
            ) && double.IsFinite(seconds)
                ? new SearchCursor(fullName, Seconds: seconds)
                : null,
            SortField.Resolution => long.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var pixels
            )
                ? new SearchCursor(fullName, Number: pixels)
                : null,
            SortField.Name or SortField.Type => new SearchCursor(fullName, value),
            SortField.Size or SortField.Modified => long.TryParse(
                value,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var number
            ) && (sort == SortField.Size || number >= 0 && number <= DateTime.MaxValue.Ticks)
                ? new SearchCursor(fullName, Number: number)
                : null,
            _ => new SearchCursor(fullName),
        };
    }

    /// <summary>
    /// The text of the cursor after <paramref name="last"/>, at <paramref name="path"/> (relative): an empty value for
    /// a file whose duration, resolution or sort name is not known, listed after the others.
    /// </summary>
    public static string Of(SortField sort, ISearchRow last, string path) =>
        sort switch
        {
            SortField.Name => $"{last.SortName}/{path}",
            SortField.Modified => $"{last.LastWriteTime.Ticks.ToString(CultureInfo.InvariantCulture)}/{path}",
            SortField.Size => $"{last.Size.ToString(CultureInfo.InvariantCulture)}/{path}",
            SortField.Type => $"{last.Extension}/{path}",
            SortField.Duration => last.Duration > 0
                ? $"{last.Duration.Value.ToString(CultureInfo.InvariantCulture)}/{path}"
                : $"/{path}",
            SortField.Resolution => last.Pixels > 0
                ? $"{last.Pixels.Value.ToString(CultureInfo.InvariantCulture)}/{path}"
                : $"/{path}",
            _ => path,
        };

    private static bool IsName(SortField sort, bool nameMayBeMissing) => nameMayBeMissing && sort == SortField.Name;
}
