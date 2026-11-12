namespace Sfe.Core.Files;

public enum FileJobKind
{
    Move,
    Copy,

    /// <summary>An archive's entries written into a folder (agents/docs/features/archive-extraction.md).</summary>
    Extract,

    /// <summary>Files and folders written into one zip in a folder (agents/docs/features/compression.md).</summary>
    Compress,
}
