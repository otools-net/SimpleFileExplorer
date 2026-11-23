using System.Collections.Concurrent;
using Otools.Net.Sfe.Core.Files;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// The space of the library's drives and of connections, kept a few minutes: every open page asks for it every few
/// minutes, and a provider counts each call. Keyed by what is measured (a drive by the library's folder, a connection
/// by its id), so every session of a space shares one read. As many entries as libraries and connections.
/// </summary>
public sealed class StorageUsageCache(TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<string, (DateTimeOffset Read, StorageUsage Usage)> _entries = new();

    /// <summary>What was read for <paramref name="key"/> within <see cref="Lifetime"/>; null otherwise.</summary>
    public StorageUsage? Find(string key) =>
        _entries.TryGetValue(key, out var entry) && time.GetUtcNow() - entry.Read < Lifetime ? entry.Usage : null;

    public StorageUsage Keep(string key, StorageUsage usage)
    {
        _entries[key] = (time.GetUtcNow(), usage);
        return usage;
    }
}
