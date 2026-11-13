namespace Sfe.Core.Connections;

/// <summary>
/// The files of one signed-in account at a provider, seen from the connection's root folder. Paths are relative to
/// it, separated by <c>/</c>, empty for the root, already normalised by <see cref="RemotePaths"/>: a provider never
/// receives <c>..</c>. What goes wrong is a <see cref="RemoteStorageException"/>, whatever the provider's own
/// errors are. A function the provider lacks (<see cref="ProviderCapabilities"/>) throws
/// <see cref="RemoteErrorKind.NotSupported"/>.
/// </summary>
public interface IRemoteStorage : IAsyncDisposable
{
    /// <summary>
    /// A page of a folder's items, in the provider's own order and page size; <paramref name="cursor"/> is the
    /// previous page's, null for the first.
    /// </summary>
    Task<RemotePage> ListAsync(string path, string? cursor, CancellationToken cancellationToken);

    /// <summary>The item at a path; null when nothing is there.</summary>
    Task<RemoteItem?> GetAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// A file's content, or the part asked for when the provider can (<see cref="RemoteContent.Range"/> says what
    /// was served).
    /// </summary>
    Task<RemoteContent> OpenReadAsync(string path, RemoteRange? range, CancellationToken cancellationToken);

    /// <summary>Starts an upload; the answer names it in the next calls.</summary>
    Task<string> BeginUploadAsync(CancellationToken cancellationToken);

    /// <summary>Adds the next part of an upload, which starts at <paramref name="offset"/> of the file.</summary>
    Task AppendUploadAsync(string upload, long offset, Stream part, CancellationToken cancellationToken);

    /// <summary>Makes the file of an upload whose parts are all sent.</summary>
    Task<RemoteItem> FinishUploadAsync(string upload, RemoteUploadTarget target, CancellationToken cancellationToken);

    Task<RemoteItem> CreateFolderAsync(string path, CancellationToken cancellationToken);

    /// <summary>Moves or renames a file, or a folder with everything in it.</summary>
    Task<RemoteItem> MoveAsync(string from, string to, RemoteConflict conflict, CancellationToken cancellationToken);

    /// <summary>Copies a file, or a folder with everything in it.</summary>
    Task<RemoteItem> CopyAsync(string from, string to, RemoteConflict conflict, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a file, or a folder with everything in it: into the provider's own trash when it has one.
    /// </summary>
    Task DeleteAsync(string path, CancellationToken cancellationToken);

    /// <summary>A small picture of a file, about <paramref name="size"/> pixels wide; null when it has none.</summary>
    Task<RemoteContent?> ThumbnailAsync(string path, int size, CancellationToken cancellationToken);

    /// <summary>
    /// What changed under the root since <paramref name="cursor"/>; with null, a cursor for "now" and no change.
    /// </summary>
    Task<RemoteChanges> ChangesAsync(string? cursor, CancellationToken cancellationToken);

    /// <summary>The space used and left; null when the provider does not tell.</summary>
    Task<RemoteSpace?> SpaceAsync(CancellationToken cancellationToken);
}
