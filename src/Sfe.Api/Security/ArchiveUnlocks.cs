using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;

namespace Sfe.Api.Security;

/// <summary>
/// Passwords of archives, held while they are explored (agents/docs/features/archive-viewer.md, "Passwords"): only in
/// the server's memory, behind an unlock id (32 random bytes) that the client sends back; never logged, never in a
/// URL, never sent back. An unlock opens one archive as it is now (path, size, time), for the one who gave the
/// password (an account and its security stamp; a visitor without one: their address), in one library; it is
/// dropped after <see cref="Idle"/> unused, at sign-out, or when the archive changes.
/// </summary>
public sealed class ArchiveUnlocks(TimeProvider time)
{
    public static readonly TimeSpan Idle = TimeSpan.FromMinutes(15);

    /// <summary>Unlocks one person keeps at once; the oldest goes past it.</summary>
    public const int MaxPerOwner = 20;

    private readonly ConcurrentDictionary<string, Unlock> _unlocks = new(StringComparer.Ordinal);

    /// <summary>Who an unlock belongs to: the account and its stamp (a new password ends its unlocks), else the address.</summary>
    public static string OwnerOf(HttpContext context) =>
        context.User.UserId() is { } user
            ? $"user:{user}:{context.User.FindFirstValue(SfeSession.StampClaim)}"
            : $"address:{context.Connection.RemoteIpAddress}";

    /// <summary>Keeps <paramref name="password"/> for the archive at <paramref name="fullPath"/> as it is now; answers the unlock id.</summary>
    public string Add(int tenant, string owner, string fullPath, string password)
    {
        Sweep();
        var mine = _unlocks.Where(pair => pair.Value.Owner == owner).OrderBy(pair => pair.Value.Used).ToList();
        foreach (var old in mine.Take(Math.Max(0, mine.Count - MaxPerOwner + 1)))
            _unlocks.TryRemove(old.Key, out _);
        var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var file = new FileInfo(fullPath);
        _unlocks[id] = new Unlock(tenant, owner, Path.GetFullPath(fullPath), file.Length, file.LastWriteTimeUtc, password) { Used = time.GetUtcNow() };
        return id;
    }

    /// <summary>
    /// The password of unlock <paramref name="id"/> when it is <paramref name="owner"/>'s, in <paramref name="tenant"/>,
    /// for the archive at <paramref name="fullPath"/> unchanged; null otherwise (and a changed archive's unlock is dropped).
    /// </summary>
    public string? PasswordFor(string? id, int tenant, string owner, string fullPath)
    {
        if (string.IsNullOrEmpty(id) || id.Length != 64 || !_unlocks.TryGetValue(id, out var unlock))
            return null;
        var now = time.GetUtcNow();
        if (now - unlock.Used > Idle)
        {
            _unlocks.TryRemove(id, out _);
            return null;
        }
        if (unlock.Tenant != tenant || unlock.Owner != owner || !string.Equals(unlock.Path, Path.GetFullPath(fullPath), StringComparison.Ordinal))
            return null;
        var file = new FileInfo(fullPath);
        if (!file.Exists || file.Length != unlock.Size || file.LastWriteTimeUtc != unlock.Modified)
        {
            _unlocks.TryRemove(id, out _);
            return null;
        }
        unlock.Used = now;
        return unlock.Password;
    }

    /// <summary>Drops every unlock of <paramref name="owner"/> (signing out).</summary>
    public void Forget(string owner)
    {
        foreach (var pair in _unlocks.Where(pair => pair.Value.Owner == owner).ToList())
            _unlocks.TryRemove(pair.Key, out _);
    }

    private void Sweep()
    {
        var now = time.GetUtcNow();
        foreach (var pair in _unlocks.Where(pair => now - pair.Value.Used > Idle).ToList())
            _unlocks.TryRemove(pair.Key, out _);
    }

    private sealed record Unlock(int Tenant, string Owner, string Path, long Size, DateTime Modified, string Password)
    {
        public DateTimeOffset Used { get; set; }
    }
}
