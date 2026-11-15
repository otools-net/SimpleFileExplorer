using System;

namespace Otools.Net.Sfe.Client;

/// <summary>
/// Which part of a file <see cref="SfeFiles.DownloadAsync"/> fetches. To go on with a download cut off earlier,
/// give the bytes already saved as <see cref="Offset"/> and the partial file, at its end, as the destination.
/// </summary>
public sealed class SfeDownloadOptions
{
    /// <summary>The first byte to fetch (0: from the start).</summary>
    public long Offset { get; set; }

    /// <summary>How many bytes to fetch; null: up to the end.</summary>
    public long? Length { get; set; }

    /// <summary>Told the bytes written to the destination by this call so far.</summary>
    public IProgress<long>? Progress { get; set; }
}
