using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Sfe.Core.Connections;

/// <summary>
/// The unfinished uploads into connections, in memory: the bytes go on to the provider's upload session as they come
/// (no file on the server's disk), so a restart loses only these records and the sender starts again. Each upload is
/// its owner's only, in the library it was started from; those idle for <see cref="IdleLimit"/> are dropped (the
/// provider ends its own sessions: Dropbox's last a week).
/// </summary>
public sealed class RemoteUploads(TimeProvider clock)
{
    /// <summary>
    /// Unfinished uploads one person may have at once, all connections together: browsers send two at a time, and
    /// each holds a session at the provider.
    /// </summary>
    public const int MaxPerOwner = 20;

    /// <summary>How long an upload is kept without a part or a finish.</summary>
    public static readonly TimeSpan IdleLimit = TimeSpan.FromHours(24);

    private static readonly TimeSpan SweepEvery = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, RemoteUpload> _uploads = new(StringComparer.Ordinal);
    private DateTimeOffset _lastSweep;

    /// <summary>The person has <see cref="MaxPerOwner"/> unfinished uploads.</summary>
    public bool IsFull(int ownerId)
    {
        Sweep();
        return _uploads.Values.Count(u => u.OwnerId == ownerId) >= MaxPerOwner;
    }

    /// <summary>Keeps a new upload whose session the provider has opened; its id is made here.</summary>
    public RemoteUpload Add(RemoteUploadStart start, string session)
    {
        var now = clock.GetUtcNow();
        var upload = new RemoteUpload
        {
            Id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)),
            ConnectionId = start.ConnectionId,
            TenantId = start.TenantId,
            OwnerId = start.OwnerId,
            OwnerName = start.OwnerName,
            Target = start.Target,
            Name = start.Name,
            Size = start.Size,
            Modified = start.Modified,
            Session = session,
            Started = now,
            LastActivity = now,
        };
        _uploads[upload.Id] = upload;
        return upload;
    }

    /// <summary>The upload when it is <paramref name="owner"/>'s, into that connection; null otherwise.</summary>
    public RemoteUpload? Find(string? id, RemoteUploadOwner owner) =>
        id is not null && _uploads.TryGetValue(id, out var upload) && IsOf(upload, owner) ? upload : null;

    /// <summary>The owner's unfinished uploads into the connection, oldest first.</summary>
    public IReadOnlyList<RemoteUpload> List(RemoteUploadOwner owner)
    {
        Sweep();
        return [.. _uploads.Values.Where(u => IsOf(u, owner)).OrderBy(u => u.Started)];
    }

    /// <summary>Notes the bytes the provider now has.</summary>
    public void Advance(RemoteUpload upload, long received)
    {
        upload.Received = received;
        upload.LastActivity = clock.GetUtcNow();
    }

    /// <summary>Forgets an upload, finished or cancelled.</summary>
    public void Remove(RemoteUpload upload) => _uploads.TryRemove(upload.Id, out _);

    /// <summary>Drops the uploads idle for longer than <see cref="IdleLimit"/>; how many.</summary>
    public int RemoveIdle()
    {
        var limit = clock.GetUtcNow() - IdleLimit;
        var idle = _uploads.Values.Where(u => u.LastActivity < limit).ToList();
        foreach (var upload in idle)
            Remove(upload);
        return idle.Count;
    }

    private static bool IsOf(RemoteUpload upload, RemoteUploadOwner owner) =>
        upload.ConnectionId == owner.ConnectionId
        && upload.TenantId == owner.TenantId
        && upload.OwnerId == owner.UserId;

    /// <summary>Drops idle uploads, at most every few minutes (when uploads are started or listed).</summary>
    private void Sweep()
    {
        var now = clock.GetUtcNow();
        if (now - _lastSweep < SweepEvery)
            return;
        _lastSweep = now;
        RemoveIdle();
    }
}
