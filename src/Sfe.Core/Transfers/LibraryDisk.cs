using Otools.Net.Sfe.Core.Files;
using static Otools.Net.Sfe.Core.Transfers.LibraryTransferErrors;

namespace Otools.Net.Sfe.Core.Transfers;

/// <summary>The disk calls of the library's side of a transfer, their failures as transfer errors.</summary>
internal static class LibraryDisk
{
    /// <summary>A new file, never over one that is there.</summary>
    public static FileStream CreateFile(string full, string name)
    {
        try
        {
            return new FileStream(
                full,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1 << 16,
                FileOptions.Asynchronous
            );
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw FromDisk(ex, $"“{name}” could not be written");
        }
    }

    /// <summary>A file opened for reading from <paramref name="offset"/> on.</summary>
    public static FileStream OpenAt(string full, long offset)
    {
        var stream = new FileStream(
            full,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1 << 16,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        if (offset > 0)
            stream.Seek(offset, SeekOrigin.Begin);
        return stream;
    }

    /// <summary>The entries of a folder, read one by one; what the disk refuses on the way is a failure.</summary>
    public static IEnumerable<FileSystemInfo> Children(string full)
    {
        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true };
        using var entries = new DirectoryInfo(full).EnumerateFileSystemInfos("*", options).GetEnumerator();
        while (true)
        {
            bool more;
            try
            {
                more = entries.MoveNext();
            }
            catch (UnauthorizedAccessException ex)
            {
                throw FromDisk(new UnreadableException(null, ex), $"“{Path.GetFileName(full)}” could not be listed");
            }
            catch (IOException ex)
            {
                throw FromDisk(ex, $"“{Path.GetFileName(full)}” could not be listed");
            }
            if (!more)
                yield break;
            yield return entries.Current;
        }
    }
}
