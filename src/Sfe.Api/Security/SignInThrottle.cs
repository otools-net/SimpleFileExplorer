using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Sfe.Core.Options;

namespace Sfe.Api.Security;

/// <summary>
/// Failed sign-ins counted per account name and address (<see cref="SecurityOptions.MaxFailedSignIns"/>): after too many,
/// that address waits <see cref="SecurityOptions.LockoutMinutes"/> before trying that name again, while the account's
/// owner signs in from anywhere else. Names that don't exist count the same, so the answers don't tell which exist. The
/// account itself is locked much later (<see cref="SecurityOptions.AccountLockoutFailures"/>, in the database), against
/// guesses from many addresses. Kept in memory: a restart forgets it, as the rate limits.
/// </summary>
public sealed class SignInThrottle(IOptionsMonitor<SecurityOptions> security)
{
    /// <summary>Entries kept at most; beyond, the expired ones go first, then all (a flood of names must not fill memory).</summary>
    public const int MaxEntries = 50_000;

    private readonly ConcurrentDictionary<(string Name, string Address), Entry> _entries = new();

    private sealed class Entry
    {
        public int Failures;
        public DateTimeOffset LastFailure;
        public DateTimeOffset? LockedUntil;
    }

    private TimeSpan Lockout => TimeSpan.FromMinutes(Math.Max(1, security.CurrentValue.LockoutMinutes));

    /// <summary>When the address may try the name again; null when it may now.</summary>
    public DateTimeOffset? LockedUntil(string? name, string address) =>
        _entries.TryGetValue(Key(name, address), out var entry) && entry.LockedUntil is { } until && until > DateTimeOffset.UtcNow ? until : null;

    /// <summary>Counts a failure; answers when the address may try again if it is now locked out.</summary>
    public DateTimeOffset? Failed(string? name, string address)
    {
        var max = security.CurrentValue.MaxFailedSignIns;
        if (max <= 0)
            return null;
        if (_entries.Count >= MaxEntries)
            Trim();

        var now = DateTimeOffset.UtcNow;
        var entry = _entries.GetOrAdd(Key(name, address), _ => new Entry());
        lock (entry)
        {
            // Failures older than a lockout are forgotten.
            if (now - entry.LastFailure > Lockout)
                entry.Failures = 0;
            entry.Failures++;
            entry.LastFailure = now;
            if (entry.Failures < max)
                return null;
            entry.Failures = 0;
            entry.LockedUntil = now + Lockout;
            return entry.LockedUntil;
        }
    }

    public void Succeeded(string? name, string address) => _entries.TryRemove(Key(name, address), out _);

    private void Trim()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, entry) in _entries)
        {
            if (!(entry.LockedUntil > now) && now - entry.LastFailure > Lockout)
                _entries.TryRemove(key, out _);
        }

        if (_entries.Count >= MaxEntries)
            _entries.Clear();
    }

    private static (string, string) Key(string? name, string address) => ((name ?? string.Empty).Trim().ToUpperInvariant(), address);
}
