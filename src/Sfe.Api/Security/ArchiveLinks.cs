using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Sfe.Api.Security;

/// <summary>A zip asked for, waiting for the browser to download it by its link.</summary>
public sealed record PendingArchive(int TenantId, string Caller, IReadOnlyList<string> Paths, DateTime Expires);

/// <summary>
/// The one-time links of zips (agents/docs/features/archives.md): the page names the items with a POST (no URL holds
/// thousands of paths, and no GET changes anything), then the browser downloads the zip from the link answered like any
/// file, streamed to disk. A link works once, for the same caller and space, within <see cref="Lifetime"/>. Also counts
/// the zips each caller is downloading, <see cref="MaxPerCaller"/> at once.
/// </summary>
public sealed class ArchiveLinks
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    /// <summary>Zips one caller may download at once (each reads the disk as fast as the network takes it).</summary>
    public const int MaxPerCaller = 2;

    private readonly ConcurrentDictionary<string, PendingArchive> _pending = new();
    private readonly ConcurrentDictionary<string, int> _running = new();
    private readonly Lock _gate = new();

    public string Add(int tenantId, string caller, IReadOnlyList<string> paths)
    {
        Purge();
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        _pending[token] = new PendingArchive(tenantId, caller, paths, DateTime.UtcNow + Lifetime);
        return token;
    }

    /// <summary>The zip of <paramref name="token"/> when it is this caller's in this space, forgotten here (once).</summary>
    public PendingArchive? Take(string token, int tenantId, string caller)
    {
        Purge();
        if (!_pending.TryGetValue(token, out var pending) || pending.TenantId != tenantId || pending.Caller != caller)
            return null;
        return _pending.TryRemove(token, out pending) ? pending : null;
    }

    /// <summary>The caller may start another zip now.</summary>
    public bool HasRoom(string caller) => _running.GetValueOrDefault(caller) < MaxPerCaller;

    /// <summary>Counts a zip of <paramref name="caller"/> until disposed; null when they already download <see cref="MaxPerCaller"/>.</summary>
    public IDisposable? TryBegin(string caller)
    {
        lock (_gate)
        {
            var running = _running.GetValueOrDefault(caller);
            if (running >= MaxPerCaller)
                return null;
            _running[caller] = running + 1;
        }
        return new Running(this, caller);
    }

    private void End(string caller)
    {
        lock (_gate)
        {
            if (_running.GetValueOrDefault(caller) <= 1)
                _running.TryRemove(caller, out _);
            else
                _running[caller]--;
        }
    }

    private void Purge()
    {
        var now = DateTime.UtcNow;
        foreach (var (token, pending) in _pending)
        {
            if (pending.Expires <= now)
                _pending.TryRemove(token, out _);
        }
    }

    private sealed class Running(ArchiveLinks links, string caller) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                links.End(caller);
        }
    }
}
