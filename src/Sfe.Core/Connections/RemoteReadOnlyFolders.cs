using System.Collections.Concurrent;

namespace Otools.Net.Sfe.Core.Connections;

/// <summary>
/// Whether a connection's folder is one its provider refuses changes in (<see cref="RemoteItem.ReadOnly"/>), asked
/// by the folder's metadata and kept briefly per folder (agents/docs/features/item-access.md): one provider call per
/// folder per <see cref="KeptFor"/> whatever the listings and pages. Only for providers with
/// <see cref="ProviderCapabilities.ReadOnlyItems"/>.
/// </summary>
public sealed class RemoteReadOnlyFolders(TimeProvider clock)
{
    /// <summary>How long an answer is kept.</summary>
    public static readonly TimeSpan KeptFor = TimeSpan.FromSeconds(30);

    /// <summary>Folders kept; past it the cache starts again.</summary>
    private const int MaxKept = 1_000;

    private readonly ConcurrentDictionary<(int Connection, string Path), (bool ReadOnly, DateTimeOffset At)> _kept =
        new();

    /// <summary>
    /// Whether the provider refuses changes in folder <paramref name="path"/> of connection
    /// <paramref name="connectionId"/>, <paramref name="ask"/>ed when not kept. An item not found, or a call that
    /// fails, counts as taking changes (quiet when unsure: the change itself says what went wrong).
    /// </summary>
    public async Task<bool> IsReadOnlyAsync(int connectionId, string path, Func<Task<RemoteItem?>> ask)
    {
        var key = (connectionId, path);
        var now = clock.GetUtcNow();
        if (_kept.TryGetValue(key, out var kept) && now - kept.At < KeptFor)
            return kept.ReadOnly;
        bool readOnly;
        try
        {
            readOnly = (await ask())?.ReadOnly == true;
        }
        catch (RemoteStorageException)
        {
            return false;
        }
        if (_kept.Count >= MaxKept)
            _kept.Clear();
        _kept[key] = (readOnly, now);
        return readOnly;
    }
}
