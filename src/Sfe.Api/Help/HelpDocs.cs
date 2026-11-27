using Otools.Net.Sfe.Api.Endpoints;

namespace Otools.Net.Sfe.Api.Help;

/// <summary>
/// The user documentation (the repository's <c>docs/</c>), built into the application as resources named
/// <c>help/&lt;path&gt;</c> (Sfe.Api.csproj): every package has it, whatever the way it is installed. Read as asked,
/// the index of titles and headings made once, on first use.
/// </summary>
public static class HelpDocs
{
    private const string Prefix = "help/";

    // Path under the help's root (with '/', any case) -> the resource's name. RecursiveDir gives '\' on Windows.
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Resources = new(ListResources);
    private static readonly Lazy<HelpIndex> IndexOnce = new(MakeIndex);

    /// <summary>The pages with their titles and headings.</summary>
    public static HelpIndex Index => IndexOnce.Value;

    /// <summary>
    /// The file at <paramref name="path"/> (<c>user-guide/browsing.md</c>); null when the help has none.
    /// </summary>
    public static Stream? Open(string path)
    {
        var key = path.Replace('\\', '/').TrimStart('/');
        return Resources.Value.TryGetValue(key, out var name)
            ? typeof(HelpDocs).Assembly.GetManifestResourceStream(name)
            : null;
    }

    private static IReadOnlyDictionary<string, string> ListResources() =>
        typeof(HelpDocs)
            .Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal))
            .ToDictionary(name => name[Prefix.Length..].Replace('\\', '/'), StringComparer.OrdinalIgnoreCase);

    private static HelpIndex MakeIndex()
    {
        var pages = Resources
            .Value.Keys.Where(path => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(SortKey, StringComparer.Ordinal)
            .Select(ReadPage)
            .ToList();
        return new HelpIndex(pages);
    }

    // README.md first in its folder, the folder's pages before its sub-folders'.
    private static string SortKey(string path)
    {
        var slash = path.LastIndexOf('/');
        var folder = slash < 0 ? "" : path[..slash];
        var name = path[(slash + 1)..];
        var first = name.Equals("README.md", StringComparison.OrdinalIgnoreCase) ? "0" : "1";
        return $"{folder.Count(c => c == '/')}\u0001{folder.ToLowerInvariant()}\u0001{first}{name.ToLowerInvariant()}";
    }

    private static HelpPage ReadPage(string path)
    {
        using var stream = Open(path)!;
        using var reader = new StreamReader(stream);
        var (title, headings) = HelpOutline.Read(reader.ReadToEnd());
        return new HelpPage(path, title ?? Path.GetFileNameWithoutExtension(path), headings);
    }
}
