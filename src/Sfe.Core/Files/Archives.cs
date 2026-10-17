using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sfe.Core.Data;
using Sfe.Core.Options;
using Sfe.Core.Paths;
using Sfe.Core.Search;

namespace Sfe.Core.Files;

/// <summary>What the person may do with a folder or file put in a zip.</summary>
public enum ArchiveVerdict
{
    /// <summary>Seen and downloadable.</summary>
    Allowed,

    /// <summary>Seen, but its files may not be opened or downloaded: left out, and said so.</summary>
    Refused,

    /// <summary>Not seen at all (folder rules): left out without a word.</summary>
    Hidden,
}

/// <summary>The access of the person downloading, asked at each item and each folder of the zip.</summary>
public interface IArchiveAccess
{
    /// <summary>Whether the root-relative <paramref name="path"/> may go in the zip (rights <c>files.view</c> and <c>files.download</c>).</summary>
    ArchiveVerdict Check(string path);

    /// <summary>The full path really is (through links) among the server's own files or a work folder: never served.</summary>
    bool IsProtected(string fullPath);

    /// <summary>As <see cref="IsProtected"/>, for a path with no link on the way (already resolved): no disk access.</summary>
    bool IsProtectedReal(string realPath);
}

/// <summary>One file or folder chosen, with the name it gets at the top of the zip.</summary>
public sealed record ArchiveItem(string Path, string FullPath, string RealPath, bool IsFolder, string EntryName);

/// <summary>What a zip holds, checked: <see cref="Archives.WriteAsync"/> writes it.</summary>
public sealed record ArchivePlan(IReadOnlyList<ArchiveItem> Items, string FileName);

public sealed record ArchivePlanResult(FileOperationStatus Status, ArchivePlan? Plan, string? Message)
{
    public static ArchivePlanResult Fail(FileOperationStatus status, string message) => new(status, null, message);
}

/// <param name="Files">Files written.</param>
/// <param name="Bytes">Their size.</param>
/// <param name="LeftOut">Root-relative paths left out (links, unreadable files, folders not downloadable), listed in the zip.</param>
public sealed record ArchiveSummary(int Files, long Bytes, IReadOnlyList<string> LeftOut);

/// <summary>A zip grew past <c>Files:MaxArchiveMegabytes</c> while it was written (the index was behind the disk).</summary>
public sealed class ArchiveTooLargeException(string message) : IOException(message);

/// <summary>
/// Several files and folders downloaded as one zip (agents/docs/features/archives.md), written straight to the
/// response while the files are read: no temporary file, nothing held in memory but the zip's directory. Media and other
/// compressed files are stored as they are, text is compressed. Links are never followed, the server's files and work
/// folders never included, and each folder is checked against the downloader's access on the way.
/// </summary>
public sealed class Archives(
    IDbContextFactory<SfeDbContext> dbFactory, LibraryPaths paths, IOptionsMonitor<SfeOptions> options, ILogger<Archives> logger)
{
    /// <summary>Items one zip may name (the folders among them bring everything inside them).</summary>
    public const int MaxItems = 10_000;

    /// <summary>The paths left out listed at most in the zip's note.</summary>
    public const int MaxLeftOutListed = 1000;

    /// <summary>Name of the note added to the zip when something was left out.</summary>
    public const string LeftOutEntry = "SFE - left out.txt";

    private const int CopyBuffer = 1024 * 1024;

    /// <summary>Extensions whose files gain from compression; everything else (media, archives, documents that are zips) is stored.</summary>
    private static readonly HashSet<string> Compressible = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".csv", ".tsv", ".json", ".xml", ".html", ".htm", ".css", ".js", ".ts", ".svg", ".log", ".ini", ".cfg", ".conf",
        ".yaml", ".yml", ".srt", ".ass", ".ssa", ".vtt", ".sub", ".nfo", ".cue", ".m3u", ".m3u8", ".pls", ".bmp", ".tif", ".tiff",
        ".wav", ".psd", ".rtf", ".sql", ".tex", ".ps1", ".sh", ".bat", ".cs", ".py", ".java", ".c", ".h", ".cpp",
    };

    /// <summary>The zip's largest size in bytes, 0 when unlimited.</summary>
    public long MaxBytes => Math.Max(0, options.CurrentValue.Files.MaxArchiveMegabytes) * 1024L * 1024;

    /// <summary>
    /// Checks the items chosen: each must exist, be seen and downloadable, outside the server's files. Items inside a
    /// folder also chosen are dropped (the folder brings them); two items of the same name (from a search) get
    /// ` (2)`.
    /// </summary>
    public ArchivePlanResult Plan(IReadOnlyList<string?>? chosen, IArchiveAccess access)
    {
        if (chosen is not { Count: > 0 and <= MaxItems })
            return ArchivePlanResult.Fail(FileOperationStatus.Invalid, $"Choose 1 to {MaxItems:N0} items.");

        var found = new List<(string Path, string Full, bool IsFolder)>();
        var seen = new HashSet<string>(LibraryPaths.PathComparer);
        foreach (var raw in chosen)
        {
            if (LibraryPaths.NormalizeRelative(raw) is not { } normalized || !paths.TryResolve(normalized, out var full))
                return ArchivePlanResult.Fail(FileOperationStatus.NotFound, "An item was not found.");
            var isFolder = Directory.Exists(full);
            if ((!isFolder && !File.Exists(full)) || access.IsProtected(full))
                return ArchivePlanResult.Fail(FileOperationStatus.NotFound, "An item was not found.");
            var path = paths.ToRelative(full)!;
            switch (access.Check(path))
            {
                case ArchiveVerdict.Hidden:
                    return ArchivePlanResult.Fail(FileOperationStatus.NotFound, "An item was not found.");
                case ArchiveVerdict.Refused:
                    return ArchivePlanResult.Fail(FileOperationStatus.Forbidden, $"You may not download “{Path.GetFileName(full)}”.");
            }
            if (seen.Add(path))
                found.Add((path, full, isFolder));
        }

        // Inside a folder chosen too: already in it.
        var folders = found.Where(f => f.IsFolder).Select(f => f.Path).ToList();
        found.RemoveAll(item => folders.Any(folder => folder != item.Path && IsInside(item.Path, folder)));

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<ArchiveItem>(found.Count);
        foreach (var (path, full, isFolder) in found)
        {
            var name = EntryName(path.Length == 0 ? paths.RootName : Path.GetFileName(full));
            var unique = name;
            for (var n = 2; !names.Add(unique); n++)
                unique = isFolder ? $"{name} ({n})" : $"{Path.GetFileNameWithoutExtension(name)} ({n}){Path.GetExtension(name)}";
            items.Add(new ArchiveItem(path, full, LibraryPaths.RealPath(full), isFolder, unique));
        }
        return new ArchivePlanResult(FileOperationStatus.Done, new ArchivePlan(items, FileName(items)), null);
    }

    /// <summary>
    /// The size of what the plan holds as the index knows it (a folder not indexed counts nothing), to refuse a zip
    /// larger than <see cref="MaxBytes"/> before it starts. Only called when there is a limit: it sums the folders' files.
    /// </summary>
    public async Task<long> IndexedSizeAsync(ArchivePlan plan, CancellationToken cancellationToken = default)
    {
        long total = 0;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        foreach (var item in plan.Items)
        {
            if (!item.IsFolder)
                total += new FileInfo(item.FullPath).Length;
            else
                total += await db.Files.AsNoTracking().Where(SearchPredicateBuilder.InTree<FileEntry>(f => f.FullName, item.FullPath))
                    .SumAsync(f => (long?)f.Size, cancellationToken) ?? 0;
        }
        return total;
    }

    /// <summary>
    /// Writes the zip of <paramref name="plan"/> to <paramref name="output"/> (which need not seek). Folders are walked
    /// as they are now, checked against <paramref name="access"/> at each one: hidden folders are left out silently,
    /// links, unreadable files and folders not downloadable are named in <see cref="LeftOutEntry"/>.
    /// </summary>
    /// <exception cref="ArchiveTooLargeException">The zip passes <see cref="MaxBytes"/>: the output is incomplete.</exception>
    public async Task<ArchiveSummary> WriteAsync(ArchivePlan plan, Stream output, IArchiveAccess access, CancellationToken cancellationToken = default)
    {
        var writer = new Writer(access, MaxBytes, logger, cancellationToken);
        var zip = await ZipArchive.CreateAsync(output, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken);
        await using (zip)
        {
            foreach (var item in plan.Items)
            {
                if (item.IsFolder)
                    await writer.AddFolderAsync(zip, item.FullPath, item.RealPath, item.Path, item.EntryName);
                else
                    await writer.AddFileAsync(zip, item.FullPath, item.Path, item.EntryName);
            }
            if (writer.LeftOut.Count > 0)
                await writer.AddNoteAsync(zip);
        }
        return new ArchiveSummary(writer.Files, writer.Bytes, writer.LeftOut);
    }

    /// <summary>A file or folder name as a zip entry: a backslash (a plain character on Linux) would be a folder on Windows.</summary>
    internal static string EntryName(string name) => name.Replace('\\', '_');

    /// <summary>The zip's name: its only item's, else the folder they are in (the library's name for the root, or for several folders).</summary>
    private string FileName(IReadOnlyList<ArchiveItem> items)
    {
        if (items.Count == 1)
            return (items[0].IsFolder ? items[0].EntryName : Path.GetFileNameWithoutExtension(items[0].EntryName)) + ".zip";
        var parents = items.Select(item => ParentOf(item.Path)).Distinct(LibraryPaths.PathComparer).ToList();
        var parent = parents.Count == 1 ? parents[0] : string.Empty;
        return EntryName(parent.Length == 0 ? paths.RootName : parent[(parent.LastIndexOf('/') + 1)..]) + ".zip";
    }

    private static string ParentOf(string path) => path.LastIndexOf('/') is var slash and >= 0 ? path[..slash] : string.Empty;

    private static bool IsInside(string path, string folder) =>
        folder.Length == 0 || (path.Length > folder.Length && path[folder.Length] == '/' && path.StartsWith(folder, LibraryPaths.PathComparison));

    private static DateTimeOffset ZipTime(DateTime local) =>
        // A zip holds 1980 to 2107.
        local.Year < 1980 ? new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Local) : local.Year > 2107 ? new DateTime(2107, 12, 31, 0, 0, 0, DateTimeKind.Local) : local;

    private sealed class Writer(IArchiveAccess access, long maxBytes, ILogger logger, CancellationToken token)
    {
        public int Files { get; private set; }
        public long Bytes { get; private set; }
        public List<string> LeftOut { get; } = [];

        public async Task AddFolderAsync(ZipArchive zip, string full, string real, string path, string entryName)
        {
            token.ThrowIfCancellationRequested();
            // Its own entry, so that an empty folder is in the zip too.
            zip.CreateEntry(entryName + "/").LastWriteTime = ZipTime(Directory.GetLastWriteTime(full));
            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(full).EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Zip: cannot read the folder {Path}", path);
                LeftOut.Add(path + "/");
                return;
            }

            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested();
                var entryPath = path.Length == 0 ? entry.Name : $"{path}/{entry.Name}";
                var entryReal = Path.Join(real, entry.Name);
                // The server's own files and the work folder: as if they weren't there.
                if (access.IsProtectedReal(entryReal))
                    continue;
                // Links are never followed (out of the library, or round in circles).
                if (entry.LinkTarget is not null)
                {
                    LeftOut.Add(entryPath);
                    continue;
                }
                var name = $"{entryName}/{EntryName(entry.Name)}";
                if (entry is DirectoryInfo)
                {
                    switch (access.Check(entryPath))
                    {
                        case ArchiveVerdict.Hidden:
                            continue;
                        case ArchiveVerdict.Refused:
                            LeftOut.Add(entryPath + "/");
                            continue;
                    }
                    await AddFolderAsync(zip, entry.FullName, entryReal, entryPath, name);
                }
                else
                {
                    await AddFileAsync(zip, entry.FullName, entryPath, name);
                }
            }
        }

        public async Task AddFileAsync(ZipArchive zip, string full, string path, string entryName)
        {
            FileStream input;
            try
            {
                input = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, CopyBuffer,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Zip: cannot read {Path}", path);
                LeftOut.Add(path);
                return;
            }

            await using (input)
            {
                if (maxBytes > 0 && Bytes + input.Length > maxBytes)
                    throw new ArchiveTooLargeException($"The zip would be larger than the {maxBytes / 1024 / 1024:N0} MB allowed.");
                var entry = zip.CreateEntry(entryName, Compressible.Contains(Path.GetExtension(full)) ? CompressionLevel.Fastest : CompressionLevel.NoCompression);
                entry.LastWriteTime = ZipTime(File.GetLastWriteTime(full));
                await using var target = await entry.OpenAsync(token);
                var buffer = new byte[CopyBuffer];
                int read;
                while ((read = await input.ReadAsync(buffer, token)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), token);
                    Bytes += read;
                }
            }
            Files++;
        }

        public async Task AddNoteAsync(ZipArchive zip)
        {
            var text = new StringBuilder("Left out of this zip (links, files that could not be read, folders you may not download):\r\n\r\n");
            foreach (var path in LeftOut.Take(MaxLeftOutListed))
                text.Append(path).Append("\r\n");
            if (LeftOut.Count > MaxLeftOutListed)
                text.Append($"… and {LeftOut.Count - MaxLeftOutListed:N0} more.\r\n");
            var entry = zip.CreateEntry(LeftOutEntry, CompressionLevel.Fastest);
            await using var target = await entry.OpenAsync(token);
            await target.WriteAsync(Encoding.UTF8.GetBytes(text.ToString()), token);
        }
    }
}
