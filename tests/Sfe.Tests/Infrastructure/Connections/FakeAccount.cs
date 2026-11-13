using Sfe.Core.Connections;

namespace Sfe.Tests.Infrastructure;

/// <summary>
/// The storage of one account of <see cref="FakeProvider"/>: files and folders by full path (from the account's
/// own root, whatever folder a connection is rooted at), and what a test makes it answer.
/// </summary>
public sealed class FakeAccount(FakeProvider provider)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, FakeEntry> _entries = new(RemotePaths.ComparerOf(provider.Capabilities));
    private readonly List<string> _changed = [];
    private long _ids;

    /// <summary>
    /// Every call fails with this until set back to null (a revoked sign-in, a provider that is down).
    /// </summary>
    public RemoteErrorKind? Failing { get; set; }

    /// <summary>For <see cref="RemoteErrorKind.RateLimited"/>: the wait asked.</summary>
    public TimeSpan? RetryAfter { get; set; }

    /// <summary>Items per page of a listing: small, so paging is always met.</summary>
    public int PageSize { get; set; } = 2;

    public long TotalSpace { get; set; } = 1024 * 1024;

    /// <summary>Paths deleted, in order: what went to the provider's own trash.</summary>
    public List<string> Trash { get; } = [];

    /// <summary>The calls made, as <c>name path</c>: tests check what reached the provider.</summary>
    public List<string> Calls { get; } = [];

    public FakeAccount WithFolder(string path)
    {
        Locked(() => PutFolder(path));
        return this;
    }

    public FakeAccount WithFile(string path, string content, DateTime? modified = null)
    {
        Locked(() => PutFile(path, System.Text.Encoding.UTF8.GetBytes(content), modified));
        return this;
    }

    public string? TextOf(string path) =>
        Locked(() => Find(path) is { Content: { } bytes } ? System.Text.Encoding.UTF8.GetString(bytes) : null);

    public bool Exists(string path) => Locked(() => path.Length == 0 || Find(path) is not null);

    internal T Locked<T>(Func<T> work)
    {
        lock (_gate)
            return work();
    }

    internal void Locked(Action work)
    {
        lock (_gate)
            work();
    }

    internal void Called(string call)
    {
        Calls.Add(call);
        if (Failing is { } kind)
            throw new RemoteStorageException(kind, $"The fake provider fails: {kind}.", RetryAfter);
    }

    internal FakeEntry? Find(string path) => _entries.GetValueOrDefault(path);

    internal IEnumerable<FakeEntry> ChildrenOf(string folder) =>
        _entries.Values.Where(e =>
            RemotePaths.ComparerOf(provider.Capabilities).Equals(RemotePaths.Parent(e.Path), folder)
        );

    internal IEnumerable<FakeEntry> TreeOf(string path) =>
        _entries.Values.Where(e => RemotePaths.IsInTree(e.Path, path, RemotePaths.ComparisonOf(provider.Capabilities)));

    internal FakeEntry PutFolder(string path)
    {
        if (path.Length == 0)
            return new FakeEntry("", null, null, "id:root");
        if (Find(path) is { } existing)
            return existing;

        PutFolder(RemotePaths.Parent(path));
        return Put(new FakeEntry(path, null, null, NextId()));
    }

    internal FakeEntry PutFile(string path, byte[] content, DateTime? modified = null)
    {
        PutFolder(RemotePaths.Parent(path));
        return Put(new FakeEntry(path, content, modified ?? DateTime.UtcNow, Find(path)?.Id ?? NextId()));
    }

    internal void Remove(string path)
    {
        foreach (var entry in TreeOf(path).ToList())
        {
            _entries.Remove(entry.Path);
            _changed.Add(entry.Path);
        }
    }

    internal long Used => _entries.Values.Sum(e => e.Content?.LongLength ?? 0);

    /// <summary>The paths changed since position <paramref name="from"/> of the change log, and its end.</summary>
    internal (IReadOnlyList<string> Paths, int Position) ChangesSince(int from) =>
        ([.. _changed.Skip(from).Distinct(RemotePaths.ComparerOf(provider.Capabilities))], _changed.Count);

    private FakeEntry Put(FakeEntry entry)
    {
        _entries[entry.Path] = entry;
        _changed.Add(entry.Path);
        return entry;
    }

    private string NextId() => $"id:{++_ids}";
}
