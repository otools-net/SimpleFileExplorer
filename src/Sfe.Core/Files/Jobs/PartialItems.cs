using Microsoft.Extensions.Logging;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Core.Files;

/// <summary>
/// The partial files and folders (<c>name.sfe-partial</c>, agents/docs/features/partial-files.md) the library's file
/// jobs write into: a free name for one, the one written now told to open listings, its final name, removing one, and
/// removing those a stopped job left.
/// </summary>
internal sealed class PartialItems(JobBoard board, LibraryBrowser browser, ILogger logger)
{
    /// <summary>
    /// The job writes <paramref name="partial"/> now, to be named <paramref name="name"/>: listed again, its progress
    /// told to open listings (<see cref="ItemsBeingWritten"/>) until <see cref="Wrote"/>.
    /// </summary>
    public void Writes(JobRun job, string partial, string name, long total)
    {
        job.Writing = browser.Writing.Begin(partial, name, total);
        browser.Invalidate(Path.GetDirectoryName(partial)!);
    }

    /// <summary>
    /// Creates <paramref name="partial"/> (a file: never one another job made); the result is its path.
    /// </summary>
    public static string Create(string partial, bool isFolder)
    {
        if (isFolder)
            Directory.CreateDirectory(partial);
        else
            new FileStream(partial, FileMode.CreateNew).Dispose();
        return partial;
    }

    /// <summary>Removes the partial file or folder <paramref name="partial"/>, whichever it is.</summary>
    public void Remove(string partial)
    {
        if (Directory.Exists(partial))
            RemoveFolder(partial);
        else
            RemoveFile(partial);
    }

    public static void Wrote(JobRun job)
    {
        job.Writing?.Dispose();
        job.Writing = null;
    }

    /// <summary>
    /// The whole copy <paramref name="partial"/> takes its name, or a keep-both one when an item of that name came
    /// meanwhile; the result is where it went.
    /// </summary>
    public static string Place(string partial, string destination, bool isFolder)
    {
        var folder = Path.GetDirectoryName(destination)!;
        var taken = LibraryClashes.Taken(folder);
        var name = Path.GetFileName(destination);
        if (taken(name))
            destination = Path.Combine(
                folder,
                FileNames.KeepBoth(name, isFolder, taken) ?? throw new IOException("No free name.")
            );
        if (isFolder)
            Directory.Move(partial, destination);
        else
            File.Move(partial, destination, overwrite: false);
        return destination;
    }

    /// <summary>
    /// A free <c>name.sfe-partial</c> in <paramref name="folder"/> (its name cut to stay a valid one), for a folder or
    /// a file.
    /// </summary>
    public static string FreeName(string folder, string stem)
    {
        stem = stem.Length > 200 ? stem[..200].TrimEnd('.', ' ') : stem;
        for (var n = 1; n <= FileNames.MaxKeepBothAttempts; n++)
        {
            var name = n == 1 ? stem + LibraryPaths.PartialSuffix : $"{stem} ({n}){LibraryPaths.PartialSuffix}";
            if (!LibraryClashes.Taken(folder)(name))
                return Path.Combine(folder, name);
        }
        throw new IOException("No free name.");
    }

    /// <summary>
    /// Partial folders an extraction left in <paramref name="folder"/> when the server stopped during it: removed at
    /// the next extraction there, unless a running job writes into one.
    /// </summary>
    public void RemoveLeftovers(JobRun job, string folder)
    {
        try
        {
            var running = board.PartialsOfOthers(job);
            foreach (var leftover in Directory.EnumerateDirectories(folder, "*" + LibraryPaths.PartialSuffix))
            {
                if (!running.Contains(leftover) && new DirectoryInfo(leftover).LinkTarget is null)
                    RemoveFolder(leftover);
            }

            foreach (var leftover in Directory.EnumerateFiles(folder, "*" + LibraryPaths.PartialSuffix))
            {
                if (!running.Contains(leftover) && new FileInfo(leftover).LinkTarget is null)
                    RemoveFile(leftover);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Could not look for partial folders in {Folder}", folder);
        }
    }

    public void RemoveFolder(string partial)
    {
        try
        {
            if (Directory.Exists(partial))
                FileTree.Delete(partial);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                ex,
                "Could not remove the partial folder {Folder}: the next extraction there does",
                partial
            );
        }
    }

    public void RemoveFile(string partial)
    {
        try
        {
            File.Delete(partial);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not remove the partial file {File}: the next compression there does", partial);
        }
    }
}
