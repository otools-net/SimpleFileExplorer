using Sfe.Core.Access;
using Sfe.Core.Indexing;
using Sfe.Core.Paths;

namespace Sfe.Core.Files;

/// <summary>
/// Name clashes of file jobs in a folder of the library (agents/docs/features/move-and-copy.md): names kept for SFE,
/// the person's choice asked when needed, the item there replaced (to the trash) or a keep-both name.
/// </summary>
public sealed class LibraryClashes(LibraryPaths paths, ScanCoordinator scans, Trash trash)
{
    /// <summary>
    /// The name the item takes in the target: itself when free, else the person's choice (asked when needed): another
    /// name, or the item there replaced (to the trash); or the item's result when it is left out.
    /// </summary>
    internal async Task<(string? Name, FileJobItem? Refused)> ClashAsync(
        JobRun job,
        ClashCheck item,
        CancellationToken token
    )
    {
        var (path, name) = (item.Path, item.Name);
        // A name no route reaches (the work folder at the root, its 8.3 name): never placed, never replaced.
        if (Reserved(item.TargetPath, name))
        {
            return (
                null,
                new FileJobItem(
                    path,
                    null,
                    FileOperationStatus.Invalid,
                    $"“{name}” was left out: that name is kept for SFE's own files."
                )
            );
        }

        var destination = Path.Combine(item.TargetFull, name);
        if (!File.Exists(destination) && !Directory.Exists(destination))
            return (name, null);
        var canReplace = await CanReplaceAsync(job, item, destination);
        var choice = item.KeepBoth
            ? ConflictChoice.KeepBoth
            : await job.ChooseAsync(new FileJobConflict(Side(item.Source), Side(destination), canReplace), token);
        switch (choice)
        {
            case ConflictChoice.Skip:
                return (
                    null,
                    new FileJobItem(
                        path,
                        null,
                        FileOperationStatus.Skipped,
                        $"“{name}” was left out: there is one of that name there."
                    )
                );
            case ConflictChoice.KeepBoth:
                return (
                    FileNames.KeepBoth(name, item.IsFolder, Taken(item.TargetFull))
                        ?? throw new IOException("No free name."),
                    null
                );
            case ConflictChoice.Replace when canReplace:
                return await ReplaceAsync(job, item, destination, token);
            default:
                return (
                    null,
                    new FileJobItem(path, null, FileOperationStatus.Forbidden, $"You may not replace “{name}” there.")
                );
        }
    }

    /// <summary>
    /// <paramref name="name"/> in <paramref name="folder"/> is a path no route takes (the work folder, its 8.3 name).
    /// </summary>
    internal bool Reserved(string folder, string name) => !paths.TryResolve(Join(folder, name), out _);

    /// <summary>A name is taken in <paramref name="folder"/>: a file or a folder has it.</summary>
    internal static Func<string, bool> Taken(string folder) =>
        n => File.Exists(Path.Combine(folder, n)) || Directory.Exists(Path.Combine(folder, n));

    internal static FileJobItem ScanRunning(string path) =>
        new(path, null, FileOperationStatus.ScanRunning, "A scan is running over it: try again when it has finished.");

    internal static string Join(string folder, string name) => folder.Length == 0 ? name : $"{folder}/{name}";

    /// <summary>
    /// The person may replace what is at <paramref name="destination"/>; never what holds the item itself (moving X/X
    /// next to X, then replacing X).
    /// </summary>
    private async Task<bool> CanReplaceAsync(JobRun job, ClashCheck item, string destination)
    {
        var replaceRight = trash.AlwaysPermanent ? Rights.DeletePermanently : Rights.DeleteFiles;
        var existingPath = Join(item.TargetPath, item.Name);
        return (item.SourcePath is null || !FolderPolicy.IsSameOrBelow(item.SourcePath, existingPath))
            && await job.Access.MayAsync(existingPath, replaceRight)
            && await job.Access.MayAsync(item.TargetPath, replaceRight)
            && (!Directory.Exists(destination) || await job.Access.MayThroughoutAsync(existingPath, replaceRight));
    }

    /// <summary>What is at <paramref name="destination"/> goes to the trash (or for good) to free the name.</summary>
    private async Task<(string? Name, FileJobItem? Refused)> ReplaceAsync(
        JobRun job,
        ClashCheck item,
        string destination,
        CancellationToken token
    )
    {
        var existingPath = Join(item.TargetPath, item.Name);
        using var hold = scans.TryBeginOperation(destination);
        if (hold is null)
            return (null, ScanRunning(item.Path));
        if (trash.AlwaysPermanent)
        {
            await trash.DeleteForGoodAsync(destination, existingPath, Directory.Exists(destination), job.Actor, token);
        }
        else
        {
            await trash.MoveInAsync(destination, existingPath, Directory.Exists(destination), job.Actor, token);
        }
        return (item.Name, null);
    }

    private static ClashSide Side(string full)
    {
        FileSystemInfo info = Directory.Exists(full) ? new DirectoryInfo(full) : new FileInfo(full);
        return new ClashSide(
            info.Name,
            info is DirectoryInfo,
            info is FileInfo file ? file.Length : null,
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)
        );
    }
}
