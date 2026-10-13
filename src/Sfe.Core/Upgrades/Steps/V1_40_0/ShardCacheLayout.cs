using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Sfe.Core.Thumbnails;
using Sfe.Core.Trickplay;

namespace Sfe.Core.Upgrades.Steps.V1_40_0;

/// <summary>
/// 1.40.0: the thumbnails and trickplay move from one folder per source folder to two levels of 256 folders.
/// </summary>
/// <remarks>
/// <para>
/// Before: <c>{cache}/{F}/{N}.bin</c> and <c>{cache}/trickplay/{F}/{N}/</c>, with <c>F</c>/<c>N</c> the hashes of the
/// file's folder and name (<see cref="ThumbnailService.Hash"/>): a large library had hundreds of thousands of folders in
/// the cache's root. After: <c>{cache}/ab/cd/{rest}.bin</c> and <c>{cache}/trickplay/ab/cd/{rest}/</c> for the key
/// <c>abcd…</c> = <see cref="ThumbnailService.CacheKey(string, string)"/> of <c>F</c> and <c>N</c>.
/// </para>
/// <para>
/// Every entry is renamed to its new place (same volume: no copy, modification times kept, so nothing is rendered
/// again); only the leftovers of interrupted renders (<c>*.tmp</c>) and the emptied old folders are deleted. Old
/// folders are recognised by their 40-character hex names (the new ones have 2), so a second run only picks up what
/// an earlier run (a failed update started again) left. An entry that cannot be moved stays where it is and is
/// reported: a cache, so the update goes on (that thumbnail or trickplay is rendered again when needed).
/// </para>
/// </remarks>
public sealed partial class ShardCacheLayout : IUpgradeStep
{
    private const int ProgressEvery = 10_000;
    private const int ListedFailures = 20;

    public SemanticVersion Version { get; } = new(1, 40, 0);

    public string Description => "Moving the thumbnails and trickplay into the new folder layout";

    public Task RunAsync(UpgradeContext context, CancellationToken cancellationToken) => Task.Run(() =>
    {
        foreach (var cache in context.CacheDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(cache))
                continue;
            var result = Move(cache, context, cancellationToken);
            if (result.Thumbnails + result.Trickplays + result.Failed > 0)
                context.Note($"{cache}: moved {result.Thumbnails} thumbnails and {result.Trickplays} trickplays"
                    + (result.Failed > 0 ? $"; {result.Failed} could not be moved and stay in the old layout." : "."));
        }
    }, cancellationToken);

    internal readonly record struct Result(int Thumbnails, int Trickplays, int Failed);

    /// <summary>Moves the entries of one cache folder (its <c>trickplay</c> sub-folder included).</summary>
    internal static Result Move(string cache, UpgradeContext context, CancellationToken cancellationToken)
    {
        var progress = new Progress(cache, context);

        foreach (var folder in OldFolders(cache))
        {
            foreach (var file in Directory.EnumerateFiles(folder.Path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(file);
                if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    progress.Try(file, () => File.Delete(file), counts: false);
                else if (name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) && IsOldHash(name[..^4]))
                    progress.Try(file, () => MoveFile(file, Path.Combine(cache, ThumbnailService.ShardedPath(Key(folder.Hash, name[..^4]))) + ".bin"));
            }

            DeleteIfEmpty(folder.Path);
        }

        var trickplay = Path.Combine(cache, TrickplayService.FolderName);
        if (Directory.Exists(trickplay))
        {
            foreach (var folder in OldFolders(trickplay))
            {
                foreach (var video in Directory.EnumerateDirectories(folder.Path))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(video);
                    if (IsOldHash(name))
                        progress.Try(video, () => MoveFolder(video, Path.Combine(trickplay, ThumbnailService.ShardedPath(Key(folder.Hash, name)))), trickplay: true);
                }

                DeleteIfEmpty(folder.Path);
            }
        }

        progress.ReportFailures();
        return new Result(progress.Thumbnails, progress.Trickplays, progress.Failed);
    }

    private static IEnumerable<(string Path, string Hash)> OldFolders(string parent) =>
        Directory.EnumerateDirectories(parent)
            .Select(path => (Path: path, Hash: Path.GetFileName(path)))
            .Where(folder => IsOldHash(folder.Hash))
            .ToList();

    private static bool IsOldHash(string name) => OldHash().IsMatch(name);

    /// <summary>The old names are upper-case hex; a file system that changed their case still gives the same key.</summary>
    private static string Key(string folderHash, string fileHash) =>
        ThumbnailService.CacheKey(folderHash.ToUpperInvariant(), fileHash.ToUpperInvariant());

    /// <summary>
    /// Renames a thumbnail. One already at the new place (rendered by this version after an update that could not move
    /// this one) is kept when it is the newer.
    /// </summary>
    private static void MoveFile(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        if (File.Exists(to) && File.GetLastWriteTimeUtc(to) >= File.GetLastWriteTimeUtc(from))
            File.Delete(from);
        else
            File.Move(from, to, overwrite: true);
    }

    /// <summary>Renames a video's trickplay folder; as <see cref="MoveFile"/>, by the date of its <c>info.json</c>.</summary>
    private static void MoveFolder(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        if (Directory.Exists(to))
        {
            if (InfoTime(to) >= InfoTime(from))
            {
                Directory.Delete(from, recursive: true);
                return;
            }

            Directory.Delete(to, recursive: true);
        }

        Directory.Move(from, to);
    }

    private static DateTime InfoTime(string folder)
    {
        var info = Path.Combine(folder, "info.json");
        return File.Exists(info) ? File.GetLastWriteTimeUtc(info) : DateTime.MinValue;
    }

    private static void DeleteIfEmpty(string folder)
    {
        try
        {
            if (!Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind: an empty folder of the old layout harms nothing.
        }
    }

    private sealed class Progress(string cache, UpgradeContext context)
    {
        private readonly List<string> _failures = [];

        public int Thumbnails { get; private set; }
        public int Trickplays { get; private set; }
        public int Failed => _failures.Count;

        public void Try(string path, Action action, bool counts = true, bool trickplay = false)
        {
            try
            {
                action();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                context.Logger.LogWarning(ex, "Could not move {Path} to the new cache layout", path);
                _failures.Add(path);
                return;
            }

            if (!counts)
                return;
            if (trickplay)
                Trickplays++;
            else
                Thumbnails++;
            if ((Thumbnails + Trickplays) % ProgressEvery == 0)
                context.Note($"{cache}: {Thumbnails + Trickplays} entries moved so far…");
        }

        public void ReportFailures()
        {
            foreach (var path in _failures.Take(ListedFailures))
                context.Note($"Could not move {path} (it stays in the old layout, unused: rendered again when needed).");
            if (_failures.Count > ListedFailures)
                context.Note($"… and {_failures.Count - ListedFailures} more (see the log).");
        }
    }

    [GeneratedRegex("^[0-9A-Fa-f]{40}$")]
    private static partial Regex OldHash();
}
