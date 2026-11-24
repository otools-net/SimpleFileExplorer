using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Otools.Net.Sfe.Core.Connections;

/// <summary>
/// The unfinished uploads into connections: the bytes go on to the provider's upload session as they come (no file on
/// the server's disk); the records are held in memory and written by <see cref="RemoteUploadRecords"/>, read back on
/// first use, so an upload goes on after a restart. Each upload is its owner's only, in the library it was started
/// from; those idle for <see cref="IdleLimit"/> are dropped (the provider ends its own sessions: Dropbox's last a
/// week), as are those of connections removed (<see cref="RemoveOfConnectionsNotIn"/>).
/// </summary>
public sealed class RemoteUploads(TimeProvider clock, RemoteUploadRecords records)
{
    /// <summary>
    /// Unfinished uploads one person may have at once, all connections together: browsers send two at a time, and
    /// each holds a session at the provider.
    /// </summary>
    public const int MaxPerOwner = 20;

    /// <summary>How long an upload is kept without a part or a finish.</summary>
    public static readonly TimeSpan IdleLimit = TimeSpan.FromHours(24);

    private static readonly TimeSpan SweepEvery = TimeSpan.FromMinutes(10);

    private readonly Lazy<ConcurrentDictionary<string, RemoteUpload>> _read = new(() =>
        new(records.ReadAll().Select(u => KeyValuePair.Create(u.Id, u)), StringComparer.Ordinal)
    );
    private DateTimeOffset _lastSweep;

    /// <summary>The uploads, read from their records on first use.</summary>
    private ConcurrentDictionary<string, RemoteUpload> Held => _read.Value;

    /// <summary>The person has <see cref="MaxPerOwner"/> unfinished uploads.</summary>
    public bool IsFull(int ownerId)
    {
        Sweep();
        return Held.Values.Count(u => u.OwnerId == ownerId) >= MaxPerOwner;
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
        records.Save(upload);
        Held[upload.Id] = upload;
        return upload;
    }

    /// <summary>The upload when it is <paramref name="owner"/>'s, into that connection; null otherwise.</summary>
    public RemoteUpload? Find(string? id, RemoteUploadOwner owner) =>
        id is not null && Held.TryGetValue(id, out var upload) && IsOf(upload, owner) ? upload : null;

    /// <summary>The owner's unfinished uploads into the connection, oldest first.</summary>
    public IReadOnlyList<RemoteUpload> List(RemoteUploadOwner owner)
    {
        Sweep();
        return [.. Held.Values.Where(u => IsOf(u, owner)).OrderBy(u => u.Started)];
    }

    /// <summary>Notes the bytes the provider now has.</summary>
    public void Advance(RemoteUpload upload, long received)
    {
        upload.Received = received;
        upload.LastActivity = clock.GetUtcNow();
        records.Save(upload);
    }

    /// <summary>Forgets an upload, finished or cancelled.</summary>
    public void Remove(RemoteUpload upload)
    {
        if (Held.TryRemove(upload.Id, out _))
            records.Remove(upload);
    }

    /// <summary>Drops the uploads idle for longer than <see cref="IdleLimit"/>; how many.</summary>
    public int RemoveIdle()
    {
        var limit = clock.GetUtcNow() - IdleLimit;
        return RemoveWhere(u => u.LastActivity < limit);
    }

    /// <summary>Drops the uploads into connections that are not in <paramref name="connections"/>; how many.</summary>
    public int RemoveOfConnectionsNotIn(IReadOnlySet<int> connections) =>
        RemoveWhere(u => !connections.Contains(u.ConnectionId));

    /// <summary>
    /// Drops the uploads that match, skipping one busy with a part or a finish (its gate held): it is in use.
    /// </summary>
    private int RemoveWhere(Func<RemoteUpload, bool> drop)
    {
        var removed = 0;
        foreach (var upload in Held.Values.Where(drop).ToList())
        {
            if (!upload.Gate.Wait(0))
                continue;
            try
            {
                if (drop(upload))
                {
                    Remove(upload);
                    removed++;
                }
            }
            finally
            {
                upload.Gate.Release();
            }
        }
        return removed;
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
