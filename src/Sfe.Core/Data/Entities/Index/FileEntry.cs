namespace Sfe.Core.Data;

public sealed class FileEntry : IndexEntry
{
    public int DirectoryId { get; set; }
    public DirectoryEntry? Directory { get; set; }
    public long Size { get; set; }

    /// <summary>Extension including the leading dot (e.g. <c>.mp4</c>); empty for a file without one.</summary>
    public string Extension { get; set; } = string.Empty;

    /// <summary>Partial content fingerprint, see <see cref="Indexing.FileFingerprint"/>.</summary>
    public string? Sha1 { get; set; }

    /// <summary>
    /// Pixel width of an image or video, as shown (turned by its orientation metadata); 0 when it could not be read,
    /// null when not read yet (other kinds of files, <see cref="Options.ScanOptions.ReadResolution"/> off, no ffmpeg).
    /// </summary>
    public int? Width { get; set; }

    /// <summary>Pixel height, see <see cref="Width"/>.</summary>
    public int? Height { get; set; }

    /// <summary>
    /// Length of a video or audio file in seconds; 0 when ffmpeg could not tell, null when not read yet (images and
    /// other files, no ffmpeg, or a video or audio file indexed before durations were read: the next scan reads it).
    /// </summary>
    public double? Duration { get; set; }

    public List<FileTag> TagLinks { get; set; } = [];
}
