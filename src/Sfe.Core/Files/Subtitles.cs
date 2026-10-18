namespace Sfe.Core.Files;

/// <summary>
/// The video a subtitle file belongs to (agents/docs/features/subtitles-viewer.md): next to it, named as it is without
/// its language and other marks (<c>Film.en.forced.srt</c> → <c>Film.en.forced</c>, <c>Film.en</c>, <c>Film</c>).
/// </summary>
public static class Subtitles
{
    /// <summary>How many marks (<c>.en</c>, <c>.forced</c>, <c>.sdh</c>) are taken off the name, at most.</summary>
    private const int MaxMarks = 3;
    private const int MaxMarkLength = 12;

    /// <summary>The names (without extension) the video may have, the closest first.</summary>
    public static IReadOnlyList<string> CandidateNames(string fileName)
    {
        var names = new List<string>();
        var name = Path.GetFileNameWithoutExtension(fileName);
        for (var marks = 0; name.Length > 0 && marks <= MaxMarks; marks++)
        {
            names.Add(name);
            var dot = name.LastIndexOf('.');
            if (dot <= 0 || name.Length - dot - 1 > MaxMarkLength)
                break;
            name = name[..dot];
        }
        return names;
    }

    /// <summary>
    /// The full path of the video next to <paramref name="subtitleFile"/> with one of <paramref name="videoExtensions"/>
    /// (without a dot), as the extension is written or in capitals; null when there is none. A few file checks, not a
    /// listing of the folder.
    /// </summary>
    public static string? FindVideo(string subtitleFile, IEnumerable<string> videoExtensions)
    {
        var folder = Path.GetDirectoryName(subtitleFile);
        if (folder is null)
            return null;
        var extensions = videoExtensions.Where(extension => extension.Length > 0).ToList();
        foreach (var name in CandidateNames(Path.GetFileName(subtitleFile)))
        {
            foreach (var extension in extensions)
            {
                foreach (var spelling in new[] { extension, extension.ToUpperInvariant() }.Distinct(StringComparer.Ordinal))
                {
                    var candidate = Path.Combine(folder, $"{name}.{spelling}");
                    // As it is written on disk (a file system ignoring case finds `film.MKV` for `film.mkv`).
                    if (File.Exists(candidate))
                        return Directory.EnumerateFiles(folder, Path.GetFileName(candidate)).FirstOrDefault() ?? candidate;
                }
            }
        }
        return null;
    }
}
