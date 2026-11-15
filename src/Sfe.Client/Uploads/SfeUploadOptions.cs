using System;

namespace Otools.Net.Sfe.Client;

/// <summary>How <see cref="SfeFiles.UploadAsync"/> and <see cref="SfeFiles.UploadFolderAsync"/> send files.</summary>
public sealed class SfeUploadOptions
{
    /// <summary>
    /// What to do when the name is taken. <see cref="SfeConflictChoice.Ask"/> (the default) fails with an
    /// <see cref="SfeConflictException"/> (<c>file-exists</c>) before sending anything;
    /// <see cref="SfeConflictChoice.Skip"/> uploads nothing and answers null.
    /// </summary>
    public SfeConflictChoice Conflicts { get; set; } = SfeConflictChoice.Ask;

    /// <summary>
    /// The size of the content, in bytes: needed for a stream that can't seek; otherwise what is left of it from its
    /// position. Not used by folder uploads.
    /// </summary>
    public long? Size { get; set; }

    /// <summary>The date of change the file gets on the server; null: the server's time of the finish.</summary>
    public DateTimeOffset? Modified { get; set; }

    /// <summary>Told the bytes the server has (of all the files, for a folder upload).</summary>
    public IProgress<long>? Progress { get; set; }

    /// <summary>
    /// Whether an unfinished upload of the same name, size and date into the same folder (one cut off earlier,
    /// by this program or another) goes on from what the server has rather than from the start; for a stream that
    /// can seek. Default true.
    /// </summary>
    public bool Resume { get; set; } = true;

    /// <summary>These options for one file of a folder upload.</summary>
    internal SfeUploadOptions ForFile(long size, DateTimeOffset modified, IProgress<long>? progress) =>
        new()
        {
            Conflicts = Conflicts,
            Size = size,
            Modified = modified,
            Progress = progress,
            Resume = Resume,
        };
}
