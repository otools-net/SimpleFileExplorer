using Microsoft.Extensions.Options;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Options;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Core.Files;

/// <summary>
/// Whether a file may be uploaded to a place of the library (<see cref="Uploads"/>), at the start and again at the
/// finish: the folder, each folder a folder upload makes on the way and the name.
/// </summary>
public sealed class UploadPlaces(LibraryPaths paths, IOptionsMonitor<SfeOptions> options)
{
    /// <summary>Folders a folder upload may name on the way to its file.</summary>
    public const int MaxDepth = 32;

    /// <summary>
    /// Why <paramref name="name"/> can't be uploaded into <paramref name="target"/> by the person: the folder, each
    /// folder on the way and the name are checked (names as new names, no <c>..</c>, the work folder refused).
    /// </summary>
    public async Task<(FileOperationResult? Error, string? Path)> CheckAsync(
        string? target,
        string? name,
        IUploadAccess access
    )
    {
        var notFound = (
            FileOperationResult.Fail(FileOperationStatus.NotFound, "The folder was not found."),
            (string?)null
        );
        if (
            LibraryPaths.NormalizeRelative(target) is not { } normalized
            || !paths.TryResolve(normalized, out var targetFull)
            || !Directory.Exists(targetFull)
            || access.IsProtected(targetFull)
            || paths.ToRelative(targetFull) is not { } targetPath
        )
        {
            return notFound;
        }

        if (!await access.MayAsync(targetPath, Rights.Browse))
            return notFound;
        if (string.IsNullOrEmpty(name))
            return (FileOperationResult.Fail(FileOperationStatus.Invalid, "A name is needed."), null);
        var segments = name.Replace('\\', '/').Split('/');
        if (segments.Length > MaxDepth)
        {
            return (
                FileOperationResult.Fail(
                    FileOperationStatus.Invalid,
                    $"“{name}” is more than {MaxDepth} folders deep."
                ),
                null
            );
        }

        if (NameProblem(targetPath, segments, access) is { } problem)
            return (problem, null);
        return await UploadRightsAsync(targetPath, segments, access) is { } refused
            ? (refused, null)
            : (null, string.Join('/', [targetPath, .. segments]).TrimStart('/'));
    }

    /// <summary>A segment that is no name, names SFE's work folder, or a name the system reads as another.</summary>
    private FileOperationResult? NameProblem(string targetPath, string[] segments, IUploadAccess access)
    {
        var path = targetPath;
        foreach (var segment in segments)
        {
            if (FileNames.Check(segment, options.CurrentValue.Files.PortableNames) is { } message)
                return FileOperationResult.Fail(FileOperationStatus.Invalid, message);
            path = Join(path, segment);
            if (
                !paths.TryResolve(path, out var full)
                || !LibraryPaths.PathComparer.Equals(Path.GetFileName(full), segment)
                || access.IsProtected(full)
            )
            {
                return FileOperationResult.Fail(
                    FileOperationStatus.Invalid,
                    $"“{segment}” is reserved for SFE's own files: pick another name."
                );
            }
        }
        return null;
    }

    /// <summary>The folder the file lands in and each folder made on the way need the right to upload.</summary>
    private static async Task<FileOperationResult?> UploadRightsAsync(
        string targetPath,
        string[] segments,
        IUploadAccess access
    )
    {
        var place = targetPath;
        if (!await access.MayAsync(place, Rights.UploadFiles))
            return FileOperationResult.Fail(FileOperationStatus.Forbidden, "You may not upload files there.");
        foreach (var segment in segments[..^1])
        {
            place = Join(place, segment);
            if (!await access.MayAsync(place, Rights.UploadFiles))
            {
                return FileOperationResult.Fail(
                    FileOperationStatus.Forbidden,
                    $"You may not upload files into “{segment}”."
                );
            }
        }
        return null;
    }

    private static string Join(string folder, string name) => folder.Length == 0 ? name : $"{folder}/{name}";
}
