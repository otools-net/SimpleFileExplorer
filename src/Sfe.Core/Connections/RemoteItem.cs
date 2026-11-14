namespace Otools.Net.Sfe.Core.Connections;

/// <summary>A file or a folder of a remote storage.</summary>
/// <param name="Path">Relative to the connection's root folder, as the provider spells it (to show).</param>
/// <param name="Size">Bytes of a file; null for a folder, and for a file the provider gives no size of.</param>
/// <param name="Modified">UTC; null when the provider keeps none (Dropbox folders).</param>
/// <param name="Id">The provider's own name for the item, stable across renames when it has one.</param>
/// <param name="Hash">The content's hash, of the kind <see cref="ProviderCapabilities.ContentHash"/> names.</param>
/// <param name="Downloadable">False for what has no content to read (an online document of the provider).</param>
public sealed record RemoteItem(
    string Name,
    string Path,
    bool IsFolder,
    long? Size = null,
    DateTime? Modified = null,
    string? Id = null,
    string? Hash = null,
    bool Downloadable = true
);
