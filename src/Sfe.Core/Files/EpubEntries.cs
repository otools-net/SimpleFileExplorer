using System.IO.Compression;
using System.Text;

namespace Sfe.Core.Files;

/// <summary>
/// The files inside an EPUB (a zip), served one by one so that the reader loads them from the app itself
/// (agents/docs/features/epub-reader.md): a book's chapters, styles, pictures and fonts, read from the zip as asked,
/// never unpacked on disk.
/// </summary>
public static class EpubEntries
{
    /// <summary>The largest entry served (a book's picture or font, never this big): a zip bomb answers nothing.</summary>
    public const long MaxEntryBytes = 64L * 1024 * 1024;

    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".xhtml"] = "application/xhtml+xml",
        [".xht"] = "application/xhtml+xml",
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".css"] = "text/css",
        [".xml"] = "application/xml",
        [".opf"] = "application/oebps-package+xml",
        [".ncx"] = "application/x-dtbncx+xml",
        [".smil"] = "application/smil+xml",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".avif"] = "image/avif",
        [".svg"] = "image/svg+xml",
        [".ttf"] = "font/ttf",
        [".otf"] = "font/otf",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".mp3"] = "audio/mpeg",
        [".m4a"] = "audio/mp4",
        [".mp4"] = "video/mp4",
        // A book's scripts never run (the reader allows none): as text, they are only ever data.
        [".js"] = "text/plain",
    };

    /// <summary>The book's path in a URL segment: base64url of its UTF-8 (relative links inside the book keep working).</summary>
    public static string Token(string relativePath) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(relativePath)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>The path a <see cref="Token"/> stands for; null for anything else.</summary>
    public static string? PathOf(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 4096)
            return null;
        var base64 = token.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static string ContentTypeOf(string entry) =>
        Types.TryGetValue(Path.GetExtension(entry), out var type) ? type : "application/octet-stream";

    /// <summary>
    /// The entry <paramref name="name"/> of the zip <paramref name="archive"/>: as written, else ignoring case (books
    /// made on Windows link <c>Images/Cover.jpg</c> to <c>images/cover.jpg</c>); null when there is none, it is a folder
    /// or it is larger than <see cref="MaxEntryBytes"/>.
    /// </summary>
    public static ZipArchiveEntry? Find(ZipArchive archive, string name)
    {
        name = name.Replace('\\', '/').TrimStart('/');
        if (name.Length == 0 || name.EndsWith('/'))
            return null;
        var entry = archive.GetEntry(name)
            ?? archive.Entries.FirstOrDefault(candidate => string.Equals(candidate.FullName, name, StringComparison.OrdinalIgnoreCase));
        return entry is null || entry.Length > MaxEntryBytes || entry.FullName.EndsWith('/') ? null : entry;
    }
}
