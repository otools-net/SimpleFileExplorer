namespace Otools.Net.Sfe.Client;

/// <summary>What the index knows below a folder: files, folders and bytes the caller sees.</summary>
public sealed class SfeFolderTotals
{
    /// <summary>Whether the folder is in the index; the counts are 0 when not.</summary>
    public bool Indexed { get; init; }

    /// <summary>Files below it.</summary>
    public long Files { get; init; }

    /// <summary>Folders below it.</summary>
    public long Folders { get; init; }

    /// <summary>Bytes of its files.</summary>
    public long Bytes { get; init; }
}
