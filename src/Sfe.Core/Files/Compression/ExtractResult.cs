namespace Sfe.Core.Files.Compression;

/// <summary>What an extraction wrote and left out.</summary>
public sealed class ExtractResult
{
    public int Files { get; internal set; }
    public int Folders { get; internal set; }
    public long Bytes { get; internal set; }

    /// <summary>
    /// The first <see cref="ArchiveExtractor.KeptLeftOut"/> entries left out; <see cref="LeftOutCount"/> counts them
    /// all.
    /// </summary>
    public List<ExtractLeftOut> LeftOut { get; } = [];

    public long LeftOutCount { get; internal set; }

    /// <summary>
    /// Why the extraction stopped before the archive's end (too many entries, the archive broken there); null when it
    /// read it all.
    /// </summary>
    public string? Stopped { get; internal set; }

    internal void Leave(string name, string reason)
    {
        if (LeftOut.Count < ArchiveExtractor.KeptLeftOut)
            LeftOut.Add(new ExtractLeftOut(name, reason));
        LeftOutCount++;
    }
}
