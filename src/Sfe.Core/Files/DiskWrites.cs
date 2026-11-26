using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Otools.Net.Sfe.Core.Files;

/// <summary>
/// Whether the server may put files in a folder, asked of the file system as it is now (never the index), without
/// writing anything: on Windows the folder is opened for adding files or folders (the system checks the ACLs, a share
/// its permissions) after the volume's read-only flag; elsewhere <c>access(2)</c> with <c>W_OK</c> (<c>EROFS</c> for
/// a read-only mount). Kept <see cref="KeptFor"/> per folder: one probe per folder, never per item
/// (agents/docs/features/item-access.md).
/// </summary>
public static class DiskWrites
{
    /// <summary>How long an answer is kept for its folder.</summary>
    public static readonly TimeSpan KeptFor = TimeSpan.FromSeconds(30);

    private const int MaxKept = 1_000;

    private static readonly ConcurrentDictionary<string, (DiskWrite Answer, DateTime At)> Kept = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
    );

    /// <summary>What the file system says of <paramref name="folder"/>, kept for a while.</summary>
    public static DiskWrite Of(string folder)
    {
        var now = DateTime.UtcNow;
        if (Kept.TryGetValue(folder, out var kept) && now - kept.At < KeptFor)
            return kept.Answer;
        var answer = Probe(folder);
        if (Kept.Count >= MaxKept)
            Kept.Clear();
        Kept[folder] = (answer, now);
        return answer;
    }

    /// <summary>What the file system says of <paramref name="folder"/> now.</summary>
    public static DiskWrite Probe(string folder)
    {
        try
        {
            return OperatingSystem.IsWindows() ? ProbeWindows(folder) : ProbeUnix(folder);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ArgumentException)
        {
            return DiskWrite.Accepted;
        }
    }

    private static DiskWrite ProbeWindows(string folder)
    {
        if (IsReadOnlyVolume(folder))
            return DiskWrite.ReadOnlyVolume;
        // Either kind of addition is enough: a drive's root often takes folders only.
        if (Opens(folder, Windows.AddFile) || Opens(folder, Windows.AddSubdirectory))
            return DiskWrite.Accepted;
        return Marshal.GetLastPInvokeError() switch
        {
            Windows.AccessDenied => DiskWrite.Refused,
            Windows.WriteProtect => DiskWrite.ReadOnlyVolume,
            _ => DiskWrite.Accepted,
        };
    }

    private static bool Opens(string folder, uint access)
    {
        using SafeFileHandle handle = Windows.CreateFileW(
            folder,
            access,
            Windows.ShareAll,
            IntPtr.Zero,
            Windows.OpenExisting,
            Windows.BackupSemantics,
            IntPtr.Zero
        );
        return !handle.IsInvalid;
    }

    private static bool IsReadOnlyVolume(string folder)
    {
        var root = new char[Windows.MaxPath];
        if (!Windows.GetVolumePathNameW(Path.GetFullPath(folder), root, (uint)root.Length))
            return false;
        var volume = new string(root, 0, Array.IndexOf(root, '\0'));
        return Windows.GetVolumeInformationW(volume, null, 0, out _, out _, out var flags, null, 0)
            && (flags & Windows.ReadOnlyVolume) != 0;
    }

    private static DiskWrite ProbeUnix(string folder)
    {
        if (Unix.Access(folder, Unix.WriteOk) == 0)
            return DiskWrite.Accepted;
        return Marshal.GetLastPInvokeError() switch
        {
            Unix.ReadOnlyFileSystem => DiskWrite.ReadOnlyVolume,
            Unix.AccessDenied or Unix.NotPermitted => DiskWrite.Refused,
            _ => DiskWrite.Accepted,
        };
    }

    private static class Windows
    {
        public const uint AddFile = 0x2;
        public const uint AddSubdirectory = 0x4;
        public const uint ShareAll = 0x7;
        public const uint OpenExisting = 3;
        public const uint BackupSemantics = 0x02000000;
        public const uint ReadOnlyVolume = 0x00080000;
        public const int AccessDenied = 5;
        public const int WriteProtect = 19;
        public const int MaxPath = 32_768;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFileW(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile
        );

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetVolumePathNameW(string fileName, [Out] char[] volumePathName, uint length);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetVolumeInformationW(
            string rootPathName,
            char[]? volumeNameBuffer,
            uint volumeNameSize,
            out uint volumeSerialNumber,
            out uint maximumComponentLength,
            out uint fileSystemFlags,
            char[]? fileSystemNameBuffer,
            uint fileSystemNameSize
        );
    }

    private static class Unix
    {
        public const int WriteOk = 2;
        public const int NotPermitted = 1;
        public const int AccessDenied = 13;
        public const int ReadOnlyFileSystem = 30;

        [DllImport("libc", EntryPoint = "access", SetLastError = true)]
        public static extern int Access(string pathName, int mode);
    }
}
