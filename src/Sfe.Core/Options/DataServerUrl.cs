using System.Text.RegularExpressions;

namespace Sfe.Core.Options;

/// <summary>
/// The <see cref="SfeOptions.DataServer"/> address: either a base the file's path is appended to
/// (<c>http://nas:8080</c>), or a template where <c>{url}</c> stands for the path (each segment percent-encoded,
/// slashes kept) and <c>{encodedUrl}</c> for the whole path percent-encoded, slashes too (for a query string):
/// <c>http://nas/get?file={encodedUrl}&amp;raw=1</c>. The client fills it in (<c>core/config.service.ts</c>).
/// </summary>
public static partial class DataServerUrl
{
    /// <summary>The value with a placeholder in it, so that nothing is appended to it.</summary>
    public static bool IsTemplate(string value) => PlaceholderRegex().IsMatch(value);

    /// <summary>
    /// Trimmed, null when empty; a plain base loses its trailing slashes (the client adds one), a template is kept
    /// as written since the slash may be wanted there.
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim();
        return IsTemplate(value) ? value : value.TrimEnd('/');
    }

    /// <summary>An http:// or https:// address once its placeholders are filled.</summary>
    public static bool IsValid(string value) =>
        Uri.TryCreate(PlaceholderRegex().Replace(value, "x"), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    [GeneratedRegex(@"\{(url|encodedUrl)\}")]
    private static partial Regex PlaceholderRegex();
}
