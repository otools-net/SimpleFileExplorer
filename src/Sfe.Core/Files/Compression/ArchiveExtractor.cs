using System.Text;

namespace Sfe.Core.Files.Compression;

/// <param name="MaxDepth">Folders nested at most (<see cref="Options.ArchiveOptions.MaxDepth"/>).</param>
/// <param name="MaxEntries">Entries read at most; past it the extraction stops and says so.</param>
/// <param name="MaxCompressionRatio">A file claiming more than this many times its packed size is left out.</param>
/// <param name="PortableNames">Names Windows refuses are left out on other systems too (<see cref="FileNames.Check"/>).</param>
/// <param name="MaxFullPath">Longest full path written (the index's limit), or none.</param>
public sealed record ExtractLimits(int MaxDepth, long MaxEntries, double MaxCompressionRatio, bool PortableNames, int? MaxFullPath = null);

/// <summary>An entry not extracted, and why.</summary>
public sealed record ExtractLeftOut(string Name, string Reason);

/// <summary>What an extraction wrote and left out.</summary>
public sealed class ExtractResult
{
    public int Files { get; internal set; }
    public int Folders { get; internal set; }
    public long Bytes { get; internal set; }

    /// <summary>The first <see cref="ArchiveExtractor.KeptLeftOut"/> entries left out; <see cref="LeftOutCount"/> counts them all.</summary>
    public List<ExtractLeftOut> LeftOut { get; } = [];

    public long LeftOutCount { get; internal set; }

    /// <summary>Why the extraction stopped before the archive's end (too many entries, the archive broken there); null when it read it all.</summary>
    public string? Stopped { get; internal set; }

    internal void Leave(string name, string reason)
    {
        if (LeftOut.Count < ArchiveExtractor.KeptLeftOut)
            LeftOut.Add(new ExtractLeftOut(name, reason));
        LeftOutCount++;
    }
}

/// <summary>
/// Writes an archive's entries (all, or those chosen) into a folder (agents/docs/features/archive-extraction.md): one
/// pass in the archive's order (<see cref="ArchiveReader.ReadThrough"/>). Every name goes through
/// <see cref="ArchiveEntryNames.Split"/> and <see cref="FileNames.Check"/>, every path is checked to stay inside the
/// folder, links are never made, each file is held to its declared size and its checksum; what can't be written is
/// left out and listed, never written elsewhere or under another name.
/// </summary>
public static class ArchiveExtractor
{
    /// <summary>Entries left out that are listed by name.</summary>
    public const int KeptLeftOut = 1000;

    private const int BufferSize = 1 << 20;

    /// <summary>
    /// Extracts <paramref name="archive"/> into the folder <paramref name="into"/> (which exists). A wrong or missing
    /// password throws (<see cref="ArchiveException.NeedsPassword"/>), as do the disk's errors; the caller removes what
    /// was written.
    /// </summary>
    /// <param name="chosen">
    /// Paths inside the archive (segments joined by <c>/</c>, as the catalog gives them) to extract with what is below
    /// them, each written from its own name (a chosen <c>a/b</c> is written as <c>b</c>); null: everything.
    /// </param>
    /// <param name="reserve">Told each file's declared size before it is written: throws when the disk can't take it.</param>
    /// <param name="read">How far into the archive's file the pass is.</param>
    /// <param name="written">Bytes written, as they are.</param>
    public static ExtractResult Extract(string archive, ArchiveFormat format, string? password, string into, IReadOnlyCollection<string>? chosen,
        ExtractLimits limits, Action<long>? reserve = null, Action<long>? read = null, Action<long>? written = null, CancellationToken cancel = default)
    {
        var root = Path.GetFullPath(into).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var picked = chosen is null ? null : new HashSet<string>(chosen, StringComparer.OrdinalIgnoreCase);
        var pickedDepth = chosen is null || chosen.Count == 0 ? 0 : chosen.Max(path => path.Count(c => c == '/') + 1);
        var result = new ExtractResult();
        var buffer = new byte[BufferSize];
        long seen = 0;
        try
        {
            ArchiveReader.ReadThrough(archive, format, password, (entry, open) =>
            {
                if (++seen > limits.MaxEntries)
                    throw new Stop($"The archive holds more than {limits.MaxEntries:N0} entries: the extraction stopped there.");
                // A .gz (or .bz2, .xz) of one file: the file is the archive's name without its extension.
                string? refused = null;
                var segments = format == ArchiveFormat.Single
                    ? [Path.GetFileNameWithoutExtension(archive)]
                    : ArchiveEntryNames.Split(entry.Name, limits.MaxDepth, out refused);
                if (segments is null)
                {
                    if (picked is null)
                        result.Leave(entry.Name, refused ?? "the name is refused");
                    return;
                }
                var start = 0;
                if (picked is not null && (start = PickedAt(segments, picked, pickedDepth)) < 0)
                    return;
                Write(entry, segments, start, open);
            }, read, cancel);
        }
        catch (Stop stop)
        {
            result.Stopped = stop.Message;
        }
        catch (ArchiveException error) when (!error.NeedsPassword)
        {
            // The archive breaks here (cut off, damaged): what was written before is whole.
            result.Stopped = $"{error.Message} The extraction stopped there.";
        }
        return result;

        void Write(ArchiveEntry entry, string[] segments, int start, Func<Stream> open)
        {
            var shown = string.Join('/', segments);
            foreach (var segment in segments.AsSpan(start))
                if (FileNames.Check(segment, limits.PortableNames) is { } wrong)
                {
                    result.Leave(shown, wrong.TrimEnd('.'));
                    return;
                }
            var full = Path.Combine([root, .. segments[start..]]);
            // Checked names can't leave the folder; checked again whatever comes.
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                result.Leave(shown, "it would be written outside of the folder");
                return;
            }
            if (limits.MaxFullPath is { } max && full.Length > max)
            {
                result.Leave(shown, $"its path would be longer than the {max} characters the index keeps");
                return;
            }
            switch (entry.Kind)
            {
                case ArchiveEntryKind.Link:
                    result.Leave(shown, "a link: links are never made");
                    return;
                case ArchiveEntryKind.Other:
                    result.Leave(shown, "neither a file nor a folder");
                    return;
                case ArchiveEntryKind.Folder:
                    // Its time isn't kept: the files written into it change it anyway.
                    if (MakeFolder(full, shown))
                        result.Folders++;
                    return;
            }
            if (entry.Packed > 0 && entry.Size / (double)entry.Packed > limits.MaxCompressionRatio)
            {
                result.Leave(shown, $"it claims to be more than {limits.MaxCompressionRatio:N0} times its packed size");
                return;
            }
            if (!MakeFolder(Path.GetDirectoryName(full)!, shown))
                return;
            if (File.Exists(full) || Directory.Exists(full))
            {
                result.Leave(shown, "an entry of the same name was extracted before it");
                return;
            }
            reserve?.Invoke(Math.Max(0, entry.Size));
            var output = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize);
            try
            {
                using (output)
                using (var data = open())
                {
                    int count;
                    while ((count = data.Read(buffer)) > 0)
                    {
                        cancel.ThrowIfCancellationRequested();
                        output.Write(buffer, 0, count);
                        written?.Invoke(count);
                    }
                }
            }
            catch (Exception error)
            {
                File.Delete(full);
                // A protected entry that doesn't decrypt: the password is wrong (not one damaged file among others).
                if (error is ArchiveException { NeedsPassword: true } || (error is ArchiveException && entry.Encrypted))
                    throw new ArchiveException("That password doesn't open this archive.", error, needsPassword: true);
                if (error is not ArchiveException)
                    throw;
                result.Leave(shown, error.Message.TrimEnd('.'));
                return;
            }
            SetTime(full, entry.Time);
            result.Files++;
            result.Bytes += Math.Max(0, entry.Size);
        }

        // Folders on the way (their own entries may come later, or never); a file in the way leaves the entry out.
        bool MakeFolder(string folder, string shown)
        {
            try
            {
                Directory.CreateDirectory(folder);
                return true;
            }
            catch (IOException) when (FileInTheWay(folder))
            {
                result.Leave(shown, "a file of the same name as one of its folders was extracted before it");
                return false;
            }
        }

        bool FileInTheWay(string folder)
        {
            for (var at = folder; at.Length > root.Length; at = Path.GetDirectoryName(at)!)
                if (File.Exists(at))
                    return true;
            return false;
        }
    }

    /// <summary>
    /// Where the entry's path starts being written: the index of the chosen item it is (or is below), so it is written
    /// from that item's own name; -1 when it is not chosen.
    /// </summary>
    private static int PickedAt(string[] segments, HashSet<string> picked, int pickedDepth)
    {
        var path = new StringBuilder();
        for (var i = 0; i < segments.Length && i < pickedDepth; i++)
        {
            if (i > 0)
                path.Append('/');
            path.Append(segments[i]);
            if (picked.Contains(path.ToString()))
                return i;
        }
        return -1;
    }

    private static void SetTime(string full, DateTime? time)
    {
        if (time is not { } value || value.Year < 1980)
            return;
        try
        {
            // Zips keep local times (no zone): read as the server's.
            var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
            File.SetLastWriteTimeUtc(full, utc);
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // A time the system refuses: the file keeps the time it was written.
        }
    }

    /// <summary>The extraction stops before the archive's end (not an error of the archive).</summary>
    private sealed class Stop(string message) : Exception(message);
}
