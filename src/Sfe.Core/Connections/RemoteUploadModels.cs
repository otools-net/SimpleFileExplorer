namespace Sfe.Core.Connections;

/// <summary>Whose uploads are asked for: a person, in a library, into one connection.</summary>
public sealed record RemoteUploadOwner(int ConnectionId, int TenantId, int UserId);

/// <summary>A new upload into a connection, checked by the route.</summary>
public sealed class RemoteUploadStart
{
    public required int ConnectionId { get; init; }

    public required int TenantId { get; init; }

    public required int OwnerId { get; init; }

    public required string OwnerName { get; init; }

    /// <summary>The folder, relative to the connection's root (normalised).</summary>
    public required string Target { get; init; }

    /// <summary>The name, or a path below <see cref="Target"/> (each part a valid name).</summary>
    public required string Name { get; init; }

    public required long Size { get; init; }

    public DateTimeOffset? Modified { get; init; }
}
