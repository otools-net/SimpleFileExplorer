using System.Collections.Concurrent;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Files;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// The space of the library's drives and of connections, kept a few minutes: every open page asks for it every few
/// minutes, and a provider counts each call. Keyed by what is measured (a drive by its library and volume folder: the
/// library's folder, a mount point, or a junction or link inside it; a connection by its id), so every session of a
/// space shares one read. As many entries as libraries, volumes reached in them and connections.
/// </summary>
/// <remarks>
/// A change made through SFE drops what was kept of where it was made, so the bar shows the space it took or freed:
/// a library's entries carry its change count when read (<see cref="Find"/> refuses an entry of another count), and a
/// connection's entry is removed when <see cref="ConnectionChanges.FolderChanged"/> tells of a change in it.
/// </remarks>
public sealed class StorageUsageCache
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, (DateTimeOffset Read, long Changes, StorageUsage Usage)> _entries =
        new();

    public StorageUsageCache(TimeProvider time, ConnectionChanges connections)
    {
        _time = time;
        connections.FolderChanged += (id, _) => _entries.TryRemove(ConnectionKey(id), out var _);
    }

    public static string ConnectionKey(int id) => "connection:" + id;

    /// <summary>
    /// What was read for <paramref name="key"/> within <see cref="Lifetime"/> and with the same
    /// <paramref name="changes"/> (a library's change count: 0 for what has none); null otherwise.
    /// </summary>
    public StorageUsage? Find(string key, long changes = 0) =>
        _entries.TryGetValue(key, out var entry)
        && entry.Changes == changes
        && _time.GetUtcNow() - entry.Read < Lifetime
            ? entry.Usage
            : null;

    /// <summary>The change count <paramref name="key"/> was kept with; null when nothing is kept (for tests).</summary>
    internal long? ChangesKept(string key) => _entries.TryGetValue(key, out var entry) ? entry.Changes : null;

    public StorageUsage Keep(string key, StorageUsage usage, long changes = 0)
    {
        _entries[key] = (_time.GetUtcNow(), changes, usage);
        return usage;
    }
}
