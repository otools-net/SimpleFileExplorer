using System.Globalization;
using System.Security.Cryptography;
using Otools.Net.Sfe.Core.Connections;

namespace Otools.Net.Sfe.Tests.Infrastructure;

/// <summary>
/// <see cref="IRemoteStorage"/> over a <see cref="FakeAccount"/>, rooted at a folder of it: what a real provider's
/// storage must do, small enough to read (the contract every provider's tests are written against).
/// </summary>
public sealed class FakeStorage(FakeAccount account, string root, ProviderCapabilities capabilities) : IRemoteStorage
{
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task<RemotePage> ListAsync(string path, string? cursor, CancellationToken cancellationToken) =>
        Call("list", path, () =>
        {
            var folder = Full(path);
            if (folder.Length > 0 && account.Find(folder) is not { IsFolder: true })
                throw NotFound(path);

            var skip = cursor is null ? 0 : int.Parse(cursor, CultureInfo.InvariantCulture);
            var items = account.ChildrenOf(folder).OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
            var next = skip + account.PageSize;
            return new RemotePage(
                [.. items.Skip(skip).Take(account.PageSize).Select(ToItem)],
                next < items.Count ? next.ToString(CultureInfo.InvariantCulture) : null
            );
        });

    public Task<RemoteItem?> GetAsync(string path, CancellationToken cancellationToken) =>
        Call("get", path, () => path.Length == 0 ? new RemoteItem("", "", true, ReadOnly: IsReadOnly(root))
            : account.Find(Full(path)) is { } entry ? ToItem(entry)
            : null);

    public async Task<RemoteContent> OpenReadAsync(string path, RemoteRange? range, CancellationToken cancellationToken)
    {
        if (account.HoldReads is { } hold)
        {
            await hold.WaitAsync(cancellationToken);
            hold.Release();
        }
        return await ReadAsync(path, range);
    }

    private Task<RemoteContent> ReadAsync(string path, RemoteRange? range) => Call("read", path, () =>
        {
            if (account.Find(Full(path)) is not { Content: { } bytes })
                throw NotFound(path);
            if (range is null || !capabilities.RangedReads)
                return new RemoteContent(new MemoryStream(bytes), bytes.Length, bytes.Length);

            var from = (int)Math.Min(range.From, bytes.Length);
            var to = (int)Math.Min(range.To ?? bytes.Length - 1, bytes.Length - 1);
            var part = bytes[from..(to + 1)];
            return new RemoteContent(new MemoryStream(part), part.Length, bytes.Length, new RemoteRange(from, to));
        });

    public Task<string> BeginUploadAsync(CancellationToken cancellationToken) => Call("upload-begin", "", () =>
        {
            var id = $"session-{Guid.NewGuid():N}";
            account.Uploads[id] = new MemoryStream();
            return id;
        });

    public async Task AppendUploadAsync(string upload, long offset, Stream part, CancellationToken cancellationToken)
    {
        var bytes = new MemoryStream();
        await part.CopyToAsync(bytes, cancellationToken);
        // As Dropbox's SDK: the part's stream is closed once sent, so a caller reading it afterwards fails here too.
        await part.DisposeAsync();
        await Call("upload-append", upload, () =>
        {
            var session = account.Uploads.GetValueOrDefault(upload) ?? throw NotFound(upload);
            if (session.Length != offset)
            {
                throw new RemoteStorageException(RemoteErrorKind.WrongOffset, $"The upload is at {session.Length}.")
                {
                    Offset = session.Length,
                };
            }
            if (bytes.Length > capabilities.MaxUploadPart)
                throw new RemoteStorageException(RemoteErrorKind.TooLarge, "The part is too large.");

            bytes.WriteTo(session);
            return true;
        });
    }

    public Task<RemoteItem> FinishUploadAsync(
        string upload,
        RemoteUploadTarget target,
        CancellationToken cancellationToken
    ) => Call("upload-finish", target.Path, () =>
        {
            var session = account.Uploads.GetValueOrDefault(upload) ?? throw NotFound(upload);
            if (session.Length != target.Length)
                throw new RemoteStorageException(RemoteErrorKind.Refused, $"The upload holds {session.Length} bytes.");
            if (account.Used + session.Length > account.TotalSpace)
                throw new RemoteStorageException(RemoteErrorKind.TooLarge, "The storage is full.");

            var path = FreePath(Full(target.Path), target.Conflict, replacing: true);
            account.Uploads.Remove(upload);
            return ToItem(account.PutFile(path, session.ToArray(), target.Modified?.UtcDateTime));
        });

    public Task<RemoteItem> CreateFolderAsync(string path, CancellationToken cancellationToken) =>
        Call("folder", path, () =>
        {
            if (account.Find(Full(path)) is not null)
                throw Exists(path);
            return ToItem(account.PutFolder(Full(path)));
        });

    public Task<RemoteItem> MoveAsync(
        string from,
        string to,
        RemoteConflict conflict,
        CancellationToken cancellationToken
    ) => Call("move", $"{from} -> {to}", () => Relocate(from, to, conflict, move: true));

    public Task<RemoteItem> CopyAsync(
        string from,
        string to,
        RemoteConflict conflict,
        CancellationToken cancellationToken
    ) => Call("copy", $"{from} -> {to}", () => Relocate(from, to, conflict, move: false));

    public Task DeleteAsync(string path, CancellationToken cancellationToken) => Call("delete", path, () =>
        {
            if (path.Length == 0 || account.Find(Full(path)) is null)
                throw NotFound(path);

            account.Remove(Full(path));
            account.Trash.Add(Full(path));
            return true;
        });

    public Task<RemoteContent?> ThumbnailAsync(string path, int size, CancellationToken cancellationToken) =>
        Call<RemoteContent?>("thumbnail", path, () =>
            capabilities.Thumbnails
                ? throw NotFound(path)
                : throw new RemoteStorageException(RemoteErrorKind.NotSupported, "No thumbnails.")
        );

    /// <summary>One call for all (as Dropbox's batch): <c>thumbnails a|b|c</c>.</summary>
    public Task<IReadOnlyList<RemoteThumbnail?>> ThumbnailsAsync(
        IReadOnlyList<string> paths,
        int size,
        CancellationToken cancellationToken
    ) => Call<IReadOnlyList<RemoteThumbnail?>>("thumbnails", string.Join("|", paths), () =>
        {
            if (!capabilities.Thumbnails)
                throw new RemoteStorageException(RemoteErrorKind.NotSupported, "No thumbnails.");
            return
            [
                .. paths.Select(path =>
                    account.Thumbnailed.TryGetValue(Full(path), out var media) && account.Find(Full(path)) is { } entry
                        ? new RemoteThumbnail(ThumbnailOf(entry), "image/jpeg", media)
                        : null
                ),
            ];
        });

    /// <summary>A fake thumbnail: its file's hash, so a test tells the versions apart.</summary>
    private byte[] ThumbnailOf(FakeEntry entry) =>
        System.Text.Encoding.UTF8.GetBytes($"thumbnail of {entry.Path} {HashOf(entry.Content ?? [])}");

    public Task<RemoteChanges> ChangesAsync(string? cursor, CancellationToken cancellationToken) =>
        Call("changes", cursor ?? "", () =>
        {
            if (cursor is not null && account.ResetCursors)
            {
                account.ResetCursors = false;
                return new RemoteChanges([], cursor, Reset: true);
            }
            var from = cursor is null ? int.MaxValue : int.Parse(cursor, CultureInfo.InvariantCulture);
            var (paths, position) = account.ChangesSince(from);
            var comparison = RemotePaths.ComparisonOf(capabilities);
            var changes = paths
                .Where(p => p.Length > root.Length && RemotePaths.IsInTree(p, root, comparison))
                .Select(p => new RemoteChange(Relative(p), account.Find(p) is { } entry ? ToItem(entry) : null));
            return new RemoteChanges([.. changes], position.ToString(CultureInfo.InvariantCulture));
        });

    public Task<RemoteSpace?> SpaceAsync(CancellationToken cancellationToken) =>
        Call("space", "", () => capabilities.FreeSpace ? new RemoteSpace(account.Used, account.TotalSpace) : null);

    private RemoteItem Relocate(string from, string to, RemoteConflict conflict, bool move)
    {
        var source = Full(from);
        if (from.Length == 0 || account.Find(source) is not { } entry)
            throw NotFound(from);

        var comparison = RemotePaths.ComparisonOf(capabilities);
        if (
            entry.IsFolder
            && RemotePaths.IsInTree(Full(to), source, comparison)
            && !Full(to).Equals(source, comparison)
        )
        {
            throw new RemoteStorageException(RemoteErrorKind.Refused, "A folder cannot go into itself.");
        }

        // A rename that only changes the case meets itself: not a conflict.
        var sameItem = move && Full(to).Equals(source, comparison);
        var target = sameItem ? Full(to) : FreePath(Full(to), conflict, replacing: !entry.IsFolder);
        var tree = account.TreeOf(source).ToList();
        if (move)
            account.Remove(source);
        foreach (var item in tree)
        {
            var path = target + item.Path[source.Length..];
            // A move keeps the items' ids (as Dropbox does); a copy gets new ones.
            var id = move ? item.Id : null;
            if (item.Content is { } bytes)
                account.PutFile(path, bytes, item.Modified, id);
            else
                account.PutFolder(path, id);
        }
        return ToItem(account.Find(target)!);
    }

    /// <summary>The path to write at, by the conflict choice; a file never replaces a folder.</summary>
    private string FreePath(string path, RemoteConflict conflict, bool replacing)
    {
        if (account.Find(path) is not { } existing)
            return path;
        if (conflict == RemoteConflict.Replace && replacing && !existing.IsFolder)
            return path;
        if (conflict != RemoteConflict.KeepBoth)
            throw Exists(Relative(path));

        var dot = RemotePaths.NameOf(path).LastIndexOf('.');
        var stem = dot > 0 ? path[..^(RemotePaths.NameOf(path).Length - dot)] : path;
        var extension = path[stem.Length..];
        return Enumerable.Range(1, 1000).Select(n => $"{stem} ({n}){extension}").First(p => account.Find(p) is null);
    }

    private Task<T> Call<T>(string name, string path, Func<T> work)
    {
        try
        {
            return Task.FromResult(
                account.Locked(() =>
                {
                    account.Called($"{name} {path}".TrimEnd());
                    return work();
                })
            );
        }
        catch (RemoteStorageException failure)
        {
            return Task.FromException<T>(failure);
        }
    }

    private string Full(string path) => RemotePaths.Combine(root, path);

    private string Relative(string full) => root.Length == 0 ? full : full[root.Length..].TrimStart('/');

    private RemoteItem ToItem(FakeEntry entry) =>
        new(
            RemotePaths.NameOf(entry.Path),
            Relative(entry.Path),
            entry.IsFolder,
            entry.Content?.LongLength,
            entry.Modified,
            entry.Id,
            entry.Content is { } bytes ? HashOf(bytes) : null,
            ReadOnly: IsReadOnly(entry.Path)
        );

    private bool IsReadOnly(string full) =>
        capabilities.ReadOnlyItems
        && account.ReadOnlyFolders.Any(folder =>
            full.Equals(folder, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)
        );

    private string HashOf(byte[] bytes) =>
        account.WrongHashes ? new string('0', 64) : Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static RemoteStorageException NotFound(string path) =>
        new(RemoteErrorKind.NotFound, $"Nothing at '{path}'.");

    private static RemoteStorageException Exists(string path) =>
        new(RemoteErrorKind.Exists, $"'{path}' is already there.");
}
