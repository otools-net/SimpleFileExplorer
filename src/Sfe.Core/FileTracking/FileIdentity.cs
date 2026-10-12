using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Sfe.Core.Paths;

namespace Sfe.Core.FileTracking;

/// <summary>
/// Whether two paths are one file, and whether two files hold the same bytes. Before a duplicate is deleted: the
/// indexer follows folder links, so a library can show the same file at two paths, and deleting either deletes both.
/// </summary>
internal static class FileIdentity
{
    private const int CompareChunk = 1024 * 1024;
    private const int MaxLinkDepth = 40;

    /// <summary>
    /// The paths lead to the same file: equal once symbolic links and junctions on the way are resolved, or, on
    /// Windows, the same file of the same volume (also catches hard links and substituted drives).
    /// </summary>
    public static bool SameFile(string a, string b)
    {
        if (LibraryPaths.PathComparer.Equals(RealPath(a), RealPath(b)))
            return true;
        return OperatingSystem.IsWindows() && WindowsFileId(a) is { } idA && WindowsFileId(b) is { } idB && idA == idB;
    }

    /// <summary>Same length and same bytes, read side by side.</summary>
    public static async Task<bool> SameContentAsync(string a, string b, CancellationToken cancellationToken)
    {
        await using var first = Open(a);
        await using var second = Open(b);
        if (first.Length != second.Length)
            return false;

        var bufferA = new byte[CompareChunk];
        var bufferB = new byte[CompareChunk];
        while (true)
        {
            var readA = await first.ReadAtLeastAsync(bufferA, CompareChunk, throwOnEndOfStream: false, cancellationToken);
            var readB = await second.ReadAtLeastAsync(bufferB, CompareChunk, throwOnEndOfStream: false, cancellationToken);
            if (readA != readB || !bufferA.AsSpan(0, readA).SequenceEqual(bufferB.AsSpan(0, readB)))
                return false;
            if (readA < CompareChunk)
                return true;
        }

        static FileStream Open(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    /// <summary>The path with every symbolic link or junction along it replaced by its final target.</summary>
    internal static string RealPath(string path, int depth = 0)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        var current = root;
        foreach (var part in full[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Join(current, part);
            FileSystemInfo info = new FileInfo(current);
            if (!info.Exists)
                info = new DirectoryInfo(current);
            if (info.LinkTarget is null || depth >= MaxLinkDepth)
                continue;
            if (info.ResolveLinkTarget(returnFinalTarget: true) is { } target)
                current = RealPath(target.FullName, depth + 1);   // the target's own folders can be links too
        }

        return Path.TrimEndingDirectorySeparator(current);
    }

    private static (uint Volume, ulong Index)? WindowsFileId(string path)
    {
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return GetFileInformationByHandle(handle, out var info)
                ? (info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);

    /// <summary>BY_HANDLE_FILE_INFORMATION; the FILETIMEs are pairs of DWORDs (4-byte aligned, unlike a long).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public uint CreationTimeLow, CreationTimeHigh;
        public uint LastAccessTimeLow, LastAccessTimeHigh;
        public uint LastWriteTimeLow, LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh, FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh, FileIndexLow;
    }
}
