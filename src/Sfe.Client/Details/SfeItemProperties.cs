namespace Otools.Net.Sfe.Client;

/// <summary>A file's or folder's properties: the item as listings show it, and what the disk tells of it.</summary>
public sealed class SfeItemProperties
{
    /// <summary>The item as listings show it.</summary>
    public SfeItem Item { get; init; } = new();

    /// <summary>Its dates and attributes on the disk.</summary>
    public SfeItemDetails Details { get; init; } = new();

    /// <summary>Whether the caller may compute its checksum (a file the caller may download).</summary>
    public bool CanChecksum { get; init; }
}
