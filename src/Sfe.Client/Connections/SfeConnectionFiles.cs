using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Otools.Net.Sfe.Client.Archives;
using Otools.Net.Sfe.Client.Connections;
using Otools.Net.Sfe.Client.Downloads;
using Otools.Net.Sfe.Client.Files;
using Otools.Net.Sfe.Client.Http;
using Otools.Net.Sfe.Client.Uploads;

namespace Otools.Net.Sfe.Client;

/// <summary>
/// The files and folders of a connection, as <see cref="SfeFiles"/> has the library's: the same calls on the
/// connection's routes, paths relative to its root folder. What its provider can't do (a part of a file, uploads,
/// moves and copies on its side) fails with <see cref="SfeNotSupportedException"/> before the call; a provider's
/// failure is an <see cref="SfeApiException"/> with its <see cref="SfeApiException.ConnectionProblem"/>.
/// </summary>
public sealed class SfeConnectionFiles
{
    private readonly SfeTransport _transport;
    private readonly SfeConnection _connection;

    internal SfeConnectionFiles(SfeTransport transport, SfeConnection connection)
    {
        _transport = transport;
        _connection = connection;
    }

    private FilePlace Place => _connection.Place;

    /// <summary>A folder's items, folders first, by name; fetched a page at a time as they are read.</summary>
    /// <param name="folder">The folder's path (<c>""</c>: the connection's root).</param>
    /// <param name="cancellationToken">Stops the listing.</param>
    /// <exception cref="SfeNotFoundException">The folder isn't there, or the caller doesn't see it.</exception>
    public IAsyncEnumerable<SfeItem> ListAsync(string folder, CancellationToken cancellationToken = default) =>
        ListAsync(folder, null, cancellationToken);

    /// <summary>
    /// A folder's items in the order asked (a folder too large to read whole comes in the provider's order); fetched
    /// a page at a time as they are read.
    /// </summary>
    /// <param name="folder">The folder's path (<c>""</c>: the connection's root).</param>
    /// <param name="options">The order and the size of the pages.</param>
    /// <param name="cancellationToken">Stops the listing.</param>
    /// <exception cref="SfeNotFoundException">The folder isn't there, or the caller doesn't see it.</exception>
    public async IAsyncEnumerable<SfeItem> ListAsync(
        string folder,
        SfeListOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        options ??= new SfeListOptions();
        var offset = 0;
        while (true)
        {
            var route = Place.PageRoute(SfePath.Normalize(folder), offset, options);
            var page = await _transport
                .ReadAsync<ConnectionPage>(SfeRequest.Get(route), cancellationToken)
                .ConfigureAwait(false);
            _connection.Learn(page.Capabilities);
            var listing = page.Listing;
            foreach (var item in listing.Items)
                yield return item;
            offset = listing.Offset + listing.Items.Count;
            // While the server reads a large folder forward, its total counts only what was read so far.
            if (listing.Items.Count == 0 || offset >= listing.Total && !page.HasMore)
                yield break;
        }
    }

    /// <summary>A file or folder as listings show it.</summary>
    /// <param name="path">Its path.</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    /// <exception cref="SfeNotFoundException">It isn't there, or the caller doesn't see it.</exception>
    public async Task<SfeItem> GetAsync(string path, CancellationToken cancellationToken = default) =>
        (await GetPropertiesAsync(path, cancellationToken).ConfigureAwait(false)).Item;

    /// <summary>Whether a file or folder is there and seen by the caller.</summary>
    /// <param name="path">Its path.</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    public async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            await GetPropertiesAsync(path, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (SfeNotFoundException)
        {
            return false;
        }
    }

    /// <summary>A file's or folder's properties: the item, and what the provider tells (its content hash…).</summary>
    /// <param name="path">Its path (<c>""</c>: the connection's root).</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    /// <exception cref="SfeNotFoundException">It isn't there, or the caller doesn't see it.</exception>
    public Task<SfeConnectionItemProperties> GetPropertiesAsync(
        string path,
        CancellationToken cancellationToken = default
    ) =>
        _transport.ReadAsync<SfeConnectionItemProperties>(
            SfeRequest.Get(Place.WithPath("files/properties", SfePath.Normalize(path))),
            cancellationToken
        );

    /// <summary>
    /// Writes a file, or the part <paramref name="options"/> names, to <paramref name="destination"/>, and answers
    /// the bytes written. An answer cut off goes on where it stopped when the provider reads parts of files.
    /// </summary>
    /// <param name="path">The file's path.</param>
    /// <param name="destination">Where its bytes are written, from the stream's position.</param>
    /// <param name="options">A part of the file, and progress; none: the whole file.</param>
    /// <param name="cancellationToken">Stops the download.</param>
    /// <exception cref="SfeNotSupportedException">A part is asked of a provider that reads files whole.</exception>
    public async Task<long> DownloadAsync(
        string path,
        Stream destination,
        SfeDownloadOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        options ??= new SfeDownloadOptions();
        var capabilities = await _connection.GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        if (options.Offset > 0 || options.Length is not null)
        {
            await _connection
                .RequireAsync(SfeConnectionCapability.RangedReads, cancellationToken)
                .ConfigureAwait(false);
        }
        // Without ranges a cut-off can't go on: it fails rather than write the file again after its start.
        var resumes = capabilities.RangedReads ? _transport.Options.MaxRetries : 0;
        var downloads = new FileDownloads(_transport, Place, resumes);
        return await downloads.DownloadAsync(path, destination, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The content of a file from <paramref name="offset"/>, read as it comes; dispose it to free the connection.
    /// </summary>
    /// <param name="path">The file's path.</param>
    /// <param name="offset">The first byte to read; past 0 only when the provider reads parts of files.</param>
    /// <param name="cancellationToken">Stops waiting for the server's answer.</param>
    /// <exception cref="SfeNotSupportedException">An offset is asked of a provider that reads files whole.</exception>
    public async Task<Stream> OpenReadAsync(string path, long offset = 0, CancellationToken cancellationToken = default)
    {
        if (offset > 0)
        {
            await _connection
                .RequireAsync(SfeConnectionCapability.RangedReads, cancellationToken)
                .ConfigureAwait(false);
        }

        var downloads = new FileDownloads(_transport, Place, 0);
        return await downloads.OpenReadAsync(path, offset, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a zip of <paramref name="paths"/> to <paramref name="destination"/> and answers the zip's name. What
    /// has no content at the provider (an online document) is named in a text entry of the zip.
    /// </summary>
    /// <param name="paths">The files and folders (1 to the server's limit).</param>
    /// <param name="destination">Where the zip is written.</param>
    /// <param name="progress">Told the bytes written so far.</param>
    /// <param name="cancellationToken">Stops the download.</param>
    public Task<string> DownloadZipAsync(
        IEnumerable<string> paths,
        Stream destination,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default
    ) =>
        ZipDownloads.DownloadAsync(
            _transport,
            Place.Route("archive"),
            paths,
            (destination, progress),
            cancellationToken
        );

    /// <summary>
    /// Uploads <paramref name="content"/> as the file <paramref name="path"/> (its folder must exist), in chunks of at
    /// most the provider's part, and answers the new file; null when the name was taken
    /// and <see cref="SfeUploadOptions.Conflicts"/> said Skip. As <see cref="SfeFiles.UploadAsync"/>, except that the
    /// server keeps an unfinished upload in memory only (a restart of the server loses it).
    /// </summary>
    /// <param name="path">The new file's path.</param>
    /// <param name="content">Its bytes, from the stream's position.</param>
    /// <param name="options">Name clashes, size, date, progress; none: the defaults.</param>
    /// <param name="cancellationToken">Stops the upload.</param>
    /// <exception cref="SfeNotSupportedException">The provider takes no uploads.</exception>
    public async Task<SfeItem?> UploadAsync(
        string path,
        Stream content,
        SfeUploadOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var uploads = await UploadsAsync(cancellationToken).ConfigureAwait(false);
        var normalized = SfePath.Normalize(path);
        return await uploads
            .UploadAsync(
                SfePath.Parent(normalized),
                SfePath.Name(normalized),
                content,
                options ?? new SfeUploadOptions(),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Uploads a folder of this computer, with everything in it, into a folder of the connection (as a folder of its
    /// name there), and answers the files made.
    /// </summary>
    /// <param name="localFolder">The folder on this computer.</param>
    /// <param name="targetFolder">The connection's folder it goes into.</param>
    /// <param name="options">Name clashes and progress (of all the files); none: the defaults.</param>
    /// <param name="cancellationToken">Stops the upload (the files already made stay).</param>
    /// <exception cref="SfeNotSupportedException">The provider takes no uploads.</exception>
    public async Task<IReadOnlyList<SfeItem>> UploadFolderAsync(
        string localFolder,
        string targetFolder,
        SfeUploadOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var uploads = await UploadsAsync(cancellationToken).ConfigureAwait(false);
        var target = SfePath.Normalize(targetFolder);
        return await uploads
            .UploadFolderAsync(localFolder, target, options ?? new SfeUploadOptions(), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Makes the folder <paramref name="path"/> (its parent must exist) and answers it.</summary>
    /// <param name="path">The new folder's path.</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    /// <exception cref="SfeConflictException"><c>file-exists</c>: something of that name is there.</exception>
    public Task<SfeItem> CreateFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        var body = new NameRequest(SfePath.Parent(path), SfePath.Name(path));
        return _transport.ReadAsync<SfeItem>(SfeRequest.Post(Place.Route("files/folders"), body), cancellationToken);
    }

    /// <summary>Renames a file or folder in its folder, and answers it; the folder rules follow it.</summary>
    /// <param name="path">Its path.</param>
    /// <param name="newName">Its new name (a name, not a path).</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    /// <exception cref="SfeConflictException"><c>file-exists</c>: the name is taken.</exception>
    public Task<SfeItem> RenameAsync(string path, string newName, CancellationToken cancellationToken = default)
    {
        var body = new NameRequest(SfePath.Normalize(path), newName);
        return _transport.ReadAsync<SfeItem>(SfeRequest.Post(Place.Route("files/rename"), body), cancellationToken);
    }

    /// <summary>
    /// Deletes files and folders into the provider's own trash when it has one (<see
    /// cref="SfeConnectionCapabilities.OwnTrash"/>; never SFE's), and answers what became of each.
    /// </summary>
    /// <param name="paths">What to delete.</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    public Task<IReadOnlyList<SfeDeleteResult>> DeleteAsync(
        IEnumerable<string> paths,
        CancellationToken cancellationToken = default
    ) => PathBatches.CollectAsync(paths, async batch =>
        {
            var request = SfeRequest.Post(Place.Route("files/delete"), new DeleteRequest(batch, false));
            var answer = await _transport.ReadAsync<DeleteAnswer>(request, cancellationToken).ConfigureAwait(false);
            return (IEnumerable<SfeDeleteResult>)answer.Items;
        });

    /// <summary>
    /// Moves files and folders into a folder of the connection, done by the provider at once (no job), and answers
    /// what became of each; their folder rules follow them.
    /// </summary>
    /// <param name="paths">What to move.</param>
    /// <param name="targetFolder">Where to.</param>
    /// <param name="options">Name clashes and folder access; none: clashes left, access gains refused.</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    /// <exception cref="SfeNotSupportedException">The provider can't move on its side.</exception>
    public Task<IReadOnlyList<SfeConnectionTransferResult>> MoveAsync(
        IEnumerable<string> paths,
        string targetFolder,
        SfeConnectionTransferOptions? options = null,
        CancellationToken cancellationToken = default
    ) => TransferAsync(SfeJobKind.Move, paths, targetFolder, options, cancellationToken);

    /// <summary>
    /// Copies files and folders into a folder of the connection, done by the provider at once (no job), and answers
    /// what became of each.
    /// </summary>
    /// <param name="paths">What to copy.</param>
    /// <param name="targetFolder">Where to.</param>
    /// <param name="options">Name clashes; none: clashes left.</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    /// <exception cref="SfeNotSupportedException">The provider can't copy on its side.</exception>
    public Task<IReadOnlyList<SfeConnectionTransferResult>> CopyAsync(
        IEnumerable<string> paths,
        string targetFolder,
        SfeConnectionTransferOptions? options = null,
        CancellationToken cancellationToken = default
    ) => TransferAsync(SfeJobKind.Copy, paths, targetFolder, options, cancellationToken);

    /// <summary>The uploads of this connection, after checking the provider takes them; chunks fit its parts.</summary>
    private async Task<FileUploads> UploadsAsync(CancellationToken cancellationToken)
    {
        await _connection.RequireAsync(SfeConnectionCapability.UploadsInParts, cancellationToken).ConfigureAwait(false);
        var capabilities = await _connection.GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        var chunk = (long)_transport.Options.UploadChunkBytes;
        if (capabilities.MaxUploadPart is { } part && part > 0 && part < chunk)
            chunk = part;
        return new FileUploads(_transport, Place, (int)chunk);
    }

    private async Task<IReadOnlyList<SfeConnectionTransferResult>> TransferAsync(
        SfeJobKind kind,
        IEnumerable<string> paths,
        string targetFolder,
        SfeConnectionTransferOptions? options,
        CancellationToken cancellationToken
    )
    {
        var needed =
            kind == SfeJobKind.Move ? SfeConnectionCapability.ServerSideMove : SfeConnectionCapability.ServerSideCopy;
        await _connection.RequireAsync(needed, cancellationToken).ConfigureAwait(false);
        options ??= new SfeConnectionTransferOptions();
        var target = SfePath.Normalize(targetFolder);
        var conflict = ConflictWord(options.Conflicts);
        return await PathBatches
            .CollectAsync(paths, async batch =>
            {
                var body = new InPlaceTransfer(kind, batch, target, conflict, options.KeepAccess);
                var request = SfeRequest.Post(Place.Route("files/transfer"), body);
                var answer = await _transport
                    .ReadAsync<InPlaceTransferAnswer>(request, cancellationToken)
                    .ConfigureAwait(false);
                return (IEnumerable<SfeConnectionTransferResult>)answer.Items;
            })
            .ConfigureAwait(false);
    }

    /// <summary>The route's word for a clash: it never waits for an answer, so Ask and Skip leave the item.</summary>
    private static string ConflictWord(SfeConflictChoice choice) =>
        choice switch
        {
            SfeConflictChoice.Replace => "replace",
            SfeConflictChoice.KeepBoth => "keepBoth",
            _ => "fail",
        };
}
