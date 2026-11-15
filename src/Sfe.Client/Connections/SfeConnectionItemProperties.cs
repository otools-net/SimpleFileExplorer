namespace Otools.Net.Sfe.Client;

/// <summary>A connection's file or folder, and what its provider tells of it.</summary>
public sealed class SfeConnectionItemProperties
{
    /// <summary>The item as listings show it (the root folder is named after the connection).</summary>
    public SfeItem Item { get; init; } = new();

    /// <summary>The provider's kind (<c>dropbox</c>).</summary>
    public string Kind { get; init; } = "";

    /// <summary>The provider's account; only on the caller's own (account-level) connection.</summary>
    public string? Account { get; init; }

    /// <summary>The file's content hash as the provider gives it; null for a folder or none.</summary>
    public string? ContentHash { get; init; }

    /// <summary>The kind of hash (<see cref="SfeConnectionCapabilities.ContentHash"/>).</summary>
    public string? HashName { get; init; }

    /// <summary>False for what has no content to read (an online document of the provider).</summary>
    public bool Downloadable { get; init; }
}
