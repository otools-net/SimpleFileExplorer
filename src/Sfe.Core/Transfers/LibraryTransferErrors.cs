using Sfe.Core.Connections;
using Sfe.Core.Files;

namespace Sfe.Core.Transfers;

/// <summary>The library side's failures, as the <see cref="RemoteStorageException"/> of both sides.</summary>
internal static class LibraryTransferErrors
{
    public static RemoteStorageException NotFound(string path) =>
        new(RemoteErrorKind.NotFound, $"“{Display(path)}” was not found in the library.");

    public static RemoteStorageException Refused(string path) =>
        new(RemoteErrorKind.Refused, $"You may not do that with “{Display(path)}” in the library.");

    public static RemoteStorageException Exists(string name) =>
        new(RemoteErrorKind.Exists, $"There is already a file or folder named “{name}” there.");

    public static RemoteStorageException InvalidName(string problem) => new(RemoteErrorKind.InvalidName, problem);

    public static RemoteStorageException ScanRunning() =>
        new(RemoteErrorKind.Unavailable, "A scan is running over it: try again when it has finished.");

    public static RemoteStorageException NoRoom(long marginBytes) =>
        new(
            RemoteErrorKind.TooLarge,
            $"The drive would keep less than {marginBytes / 1024 / 1024:N0} MB free (the library's minimum)."
        );

    /// <summary>What the disk refused, with the words the library's own operations use.</summary>
    public static RemoteStorageException FromDisk(Exception ex, string what)
    {
        var kind = ex switch
        {
            UnauthorizedAccessException => RemoteErrorKind.Refused,
            IOException io when IsDiskFull(io) => RemoteErrorKind.TooLarge,
            _ => RemoteErrorKind.Unavailable,
        };
        var message =
            kind == RemoteErrorKind.TooLarge
                ? $"{what}: the drive is full."
                : FileOperations.DiskFailure(ex, what).Message!;
        return new RemoteStorageException(kind, message, inner: ex);
    }

    /// <summary>The disk is full: ERROR_DISK_FULL or ERROR_HANDLE_DISK_FULL (Windows), ENOSPC (elsewhere).</summary>
    private static bool IsDiskFull(IOException ex) =>
        OperatingSystem.IsWindows() ? (ex.HResult & 0xFFFF) is 112 or 39 : ex.HResult == 28;

    private static string Display(string path) => path.Length == 0 ? "/" : path;
}
