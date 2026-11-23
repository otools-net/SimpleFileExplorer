using System.Runtime.InteropServices;

namespace Otools.Net.Sfe.Core.Files;

/// <summary>
/// The size and free space of the file system holding a folder. On Windows through <c>GetDiskFreeSpaceEx</c>, which
/// takes any folder (a share's <c>\\server\share</c>, a folder a drive is mounted on) where <see cref="DriveInfo"/>
/// wants a drive letter; elsewhere <see cref="DriveInfo"/> on the folder itself, whose <c>statvfs</c> answers for
/// the file system holding it (a Docker volume under <c>/data</c>, not <c>/</c>).
/// </summary>
public static class DiskSpace
{
    /// <summary>The space of the file system of <paramref name="folder"/>; not reported if it can't be read.</summary>
    public static StorageUsage Of(string folder)
    {
        try
        {
            var full = Path.GetFullPath(folder);
            if (OperatingSystem.IsWindows())
                return NativeMethods.GetDiskFreeSpaceExW(EndingInSeparator(full), out var free, out var total, out _)
                    ? StorageUsage.OfDrive((long)total, (long)free)
                    : StorageUsage.NotReported;
            var drive = new DriveInfo(full);
            return StorageUsage.OfDrive(drive.TotalSize, drive.AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return StorageUsage.NotReported;
        }
    }

    // GetDiskFreeSpaceEx wants a share's root with its trailing separator.
    private static string EndingInSeparator(string path) =>
        Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetDiskFreeSpaceExW(
            string directoryName,
            out ulong freeBytesAvailable,
            out ulong totalNumberOfBytes,
            out ulong totalNumberOfFreeBytes
        );
    }
}
