namespace Otools.Net.Sfe.Core.Connections;

/// <summary>
/// A <see cref="RemoteUpload"/> as <see cref="RemoteUploadRecords"/> writes it: the provider's session protected as
/// the connections' credentials are (<see cref="ICredentialProtector"/>), so the file alone gives nobody the session.
/// </summary>
public sealed class RemoteUploadRecord
{
    public required string Id { get; init; }

    public required int ConnectionId { get; init; }

    public required int TenantId { get; init; }

    public required int OwnerId { get; init; }

    public required string OwnerName { get; init; }

    public required string Target { get; init; }

    public required string Name { get; init; }

    public required long Size { get; init; }

    public DateTimeOffset? Modified { get; init; }

    /// <summary>The provider's upload session, protected.</summary>
    public required string ProtectedSession { get; init; }

    public required DateTimeOffset Started { get; init; }

    public long Received { get; init; }

    public DateTimeOffset LastActivity { get; init; }
}
