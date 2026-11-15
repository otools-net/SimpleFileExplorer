namespace Otools.Net.Sfe.Client;

/// <summary>What a connection's provider can do: calls needing what it lacks are refused before being sent.</summary>
public sealed class SfeConnectionCapabilities
{
    /// <summary>A part of a file can be read; without it a file is always read whole, from its start.</summary>
    public bool RangedReads { get; init; }

    /// <summary>Files can be uploaded (in parts).</summary>
    public bool UploadsInParts { get; init; }

    /// <summary>The largest part of an upload, in bytes; null when the provider sets no limit.</summary>
    public long? MaxUploadPart { get; init; }

    /// <summary>Copies inside the connection are done by the provider.</summary>
    public bool ServerSideCopy { get; init; }

    /// <summary>Moves inside the connection are done by the provider.</summary>
    public bool ServerSideMove { get; init; }

    /// <summary>The provider makes previews of pictures (through <see cref="SfeClient.Raw"/>).</summary>
    public bool Thumbnails { get; init; }

    /// <summary>The name of the content hash it gives (<c>dropbox</c>, <c>sha256</c>…); null for none.</summary>
    public string? ContentHash { get; init; }

    /// <summary>What is deleted goes to the provider's own trash (undone there, not in SFE).</summary>
    public bool OwnTrash { get; init; }

    /// <summary>Names differing only by case are the same item.</summary>
    public bool CaseInsensitiveNames { get; init; }

    /// <summary>Whether it can do <paramref name="capability"/>.</summary>
    /// <param name="capability">What a call needs.</param>
    public bool Has(SfeConnectionCapability capability) =>
        capability switch
        {
            SfeConnectionCapability.RangedReads => RangedReads,
            SfeConnectionCapability.UploadsInParts => UploadsInParts,
            SfeConnectionCapability.ServerSideCopy => ServerSideCopy,
            SfeConnectionCapability.ServerSideMove => ServerSideMove,
            _ => false,
        };
}
