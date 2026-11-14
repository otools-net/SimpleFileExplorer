using Sfe.Core.Files;

namespace Sfe.Core.Connections;

/// <summary>
/// An unfinished upload into a connection, kept by <see cref="RemoteUploads"/>: what <see cref="UploadInfo"/> shows,
/// plus whose it is and the provider's session, which stays on the server.
/// </summary>
public sealed class RemoteUpload
{
    /// <summary>SFE's id: 32 lowercase hexadecimal digits, never the provider's.</summary>
    public required string Id { get; init; }

    public required int ConnectionId { get; init; }

    /// <summary>The library the upload was started from: it is only seen from there.</summary>
    public required int TenantId { get; init; }

    public required int OwnerId { get; init; }

    public required string OwnerName { get; init; }

    /// <summary>The folder it goes into, relative to the connection's root (normalised).</summary>
    public required string Target { get; init; }

    /// <summary>The file's name, or its path below <see cref="Target"/> for a folder upload.</summary>
    public required string Name { get; init; }

    public required long Size { get; init; }

    /// <summary>The file's date on the uploader's computer.</summary>
    public DateTimeOffset? Modified { get; init; }

    /// <summary>The provider's upload session: never in an answer, a log line or the client.</summary>
    public required string Session { get; init; }

    public required DateTimeOffset Started { get; init; }

    /// <summary>Bytes the provider has: the next part starts there.</summary>
    public long Received { get; set; }

    public DateTimeOffset LastActivity { get; set; }

    /// <summary>One part or finish at a time.</summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);

    /// <summary>What the uploader's browser is answered.</summary>
    public UploadInfo ToInfo() =>
        new(Id, Target, Name, Size, Received, Modified, OwnerId, OwnerName, Started, LastActivity);
}
