using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Sfe.Core.Connections;

/// <summary>
/// Sign-ins waiting for the person to come back from the provider, kept on the server only (the provider's
/// <see cref="SignInStep.Pending"/>, a PKCE verifier, never reaches the browser). Found by their random state, bound to
/// the account that started them, used once, forgotten after <see cref="Lifetime"/>. A restart forgets them: the
/// person starts again.
/// </summary>
public sealed class PendingSignIns(TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>At most this many wait at once (each person rarely has more than one).</summary>
    public const int MaxPending = 1000;

    private readonly ConcurrentDictionary<string, PendingSignIn> _byState = new(StringComparer.Ordinal);

    /// <summary>A state nobody can guess, for <see cref="Add"/>.</summary>
    public static string NewState() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <returns>False when too many are waiting.</returns>
    public bool Add(PendingSignIn signIn)
    {
        Sweep();
        if (_byState.Count >= MaxPending)
            return false;
        _byState[signIn.State] = signIn with { Expires = clock.GetUtcNow() + Lifetime };
        return true;
    }

    /// <summary>
    /// The sign-in of <paramref name="state"/>, removed: a state is used once, whoever brings it. Null when unknown,
    /// late, or started by another account than <paramref name="userId"/> (also removed then: a state seen by
    /// someone else is spent).
    /// </summary>
    public PendingSignIn? Take(string? state, int userId)
    {
        if (string.IsNullOrEmpty(state) || !_byState.TryRemove(state, out var signIn))
            return null;
        return signIn.UserId == userId && signIn.Expires > clock.GetUtcNow() ? signIn : null;
    }

    public int Count => _byState.Count;

    private void Sweep()
    {
        var now = clock.GetUtcNow();
        foreach (var (state, signIn) in _byState)
        {
            if (signIn.Expires <= now)
                _byState.TryRemove(state, out _);
        }
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
