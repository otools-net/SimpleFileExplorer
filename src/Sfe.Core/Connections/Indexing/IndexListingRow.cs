namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// A folder or a file of a connection's index as a listing reads it (<see cref="RemoteIndexListings"/>): one shape
/// for both tables, so that one ordering and one key paging serve both. Ordered and compared in the database.
/// </summary>
internal sealed class IndexListingRow
{
    public bool IsFolder { get; init; }

    /// <summary>The key (<see cref="Data.RemoteFile.FullName"/>): unique, the tie of every sort.</summary>
    public string FullName { get; init; } = string.Empty;

    public string DisplayPath { get; init; } = string.Empty;

    /// <summary>The name as listings sort it (<see cref="Data.RemoteFile.NameKey"/>).</summary>
    public string? NameKey { get; init; }

    /// <summary>The extension with its dot; empty for a folder.</summary>
    public string Extension { get; init; } = string.Empty;

    /// <summary>0 for a folder.</summary>
    public long Size { get; init; }

    /// <summary>UTC; null for a folder the provider keeps no date of.</summary>
    public DateTime? Modified { get; init; }

    public string? ProviderId { get; init; }

    public string? Hash { get; init; }

    public bool Downloadable { get; init; }

    /// <summary>The item as the provider's listings give it.</summary>
    public RemoteItem ToItem() =>
        new(
            RemotePaths.NameOf(DisplayPath),
            DisplayPath,
            IsFolder,
            IsFolder ? null : Size,
            Modified is { } date && date != default ? DateTime.SpecifyKind(date, DateTimeKind.Utc) : null,
            ProviderId,
            Hash,
            Downloadable
        );
}
