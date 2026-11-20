using Microsoft.Extensions.Logging;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Core.Files;

/// <summary>
/// The partial files and folders (<c>name.sfe-partial</c>, agents/docs/features/partial-files.md) the library's file
/// jobs write into: a free name for one, removing one, and removing those a stopped job left.
/// </summary>
internal sealed class PartialItems(JobBoard board, ILogger logger)
{
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
