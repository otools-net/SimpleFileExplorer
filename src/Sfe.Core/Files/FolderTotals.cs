namespace Sfe.Core.Files;

/// <summary>What the index knows below a folder (the files and folders the asker may see).</summary>
/// <param name="Indexed">The folder itself is in the index (else the counts are those of what was indexed below it, if
/// anything).</param>
public sealed record FolderTotals(bool Indexed, long Files, long Folders, long Bytes);
