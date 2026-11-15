using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Otools.Net.Sfe.Client.Downloads;
using Otools.Net.Sfe.Client.Files;
using Otools.Net.Sfe.Client.Http;
using Otools.Net.Sfe.Client.Jobs;
using Otools.Net.Sfe.Client.Uploads;

namespace Otools.Net.Sfe.Client;

/// <summary>
/// The files and folders of the library: listed, read, uploaded, made, renamed, moved, copied and deleted. Paths are
/// the API's (<see cref="SfePath"/>).
/// </summary>
public sealed class SfeFiles
{
    /// <summary>Paths one delete request may name.</summary>
    private const int DeleteBatch = 1000;

    private readonly SfeTransport _transport;
    private readonly SfeProperties _properties;
    private readonly FileDownloads _downloads;
    private readonly FileUploads _uploads;

    internal SfeFiles(SfeTransport transport, SfeProperties properties)
    {
        _transport = transport;
        _properties = properties;
        _downloads = new FileDownloads(transport);
        _uploads = new FileUploads(transport);
    }

    /// <summary>A folder's items, folders first, by name; fetched a page at a time as they are read.</summary>
    /// <param name="folder">The folder's path (<c>""</c>: the root).</param>
    /// <param name="cancellationToken">Stops the listing.</param>
    /// <exception cref="SfeNotFoundException">The folder isn't there, or the caller doesn't see it.</exception>
    public IAsyncEnumerable<SfeItem> ListAsync(string folder, CancellationToken cancellationToken = default) =>
        ListAsync(folder, null, cancellationToken);

    /// <summary>
    /// A folder's items, folders first, in the order asked; fetched a page at a time as they are read.
    /// </summary>
    /// <param name="folder">The folder's path (<c>""</c>: the root).</param>
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
            var route = PageRoute(SfePath.Normalize(folder), offset, options);
            var page = await _transport
                .ReadAsync<ListingPage>(SfeRequest.Get(route), cancellationToken)
                .ConfigureAwait(false);
            foreach (var item in page.Items)
                yield return item;
            offset = page.Offset + page.Items.Count;
            if (page.Items.Count == 0 || offset >= page.Total)
                yield break;
        }
    }

    /// <summary>A file or folder as listings show it.</summary>
    /// <param name="path">Its path.</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    /// <exception cref="SfeNotFoundException">It isn't there, or the caller doesn't see it.</exception>
    public async Task<SfeItem> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        var properties = await _properties.GetAsync(path, cancellationToken).ConfigureAwait(false);
        return properties.Item;
    }

    /// <summary>Whether a file or folder is there and seen by the caller.</summary>
    /// <param name="path">Its path.</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    public async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            await _properties.GetAsync(path, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (SfeNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes a file, or the part <paramref name="options"/> names, to <paramref name="destination"/>; an answer cut
    /// off on the way goes on where it stopped (as long as the file is unchanged). Answers the bytes written.
    /// </summary>
    /// <param name="path">The file's path.</param>
    /// <param name="destination">Where its bytes are written, from the stream's position.</param>
    /// <param name="options">A part of the file, and progress; none: the whole file.</param>
    /// <param name="cancellationToken">Stops the download.</param>
    public Task<long> DownloadAsync(
        string path,
        Stream destination,
        SfeDownloadOptions? options = null,
        CancellationToken cancellationToken = default
    ) => _downloads.DownloadAsync(path, destination, options ?? new SfeDownloadOptions(), cancellationToken);

    /// <summary>
    /// The content of a file from <paramref name="offset"/>, read as it comes; dispose it to free the connection.
    /// </summary>
    /// <param name="path">The file's path.</param>
    /// <param name="offset">The first byte to read.</param>
    /// <param name="cancellationToken">Stops waiting for the server's answer.</param>
    public Task<Stream> OpenReadAsync(string path, long offset = 0, CancellationToken cancellationToken = default) =>
        _downloads.OpenReadAsync(path, offset, cancellationToken);

    /// <summary>
    /// Uploads <paramref name="content"/> as the file <paramref name="path"/> (its folder must exist), in chunks that
    /// survive a cut-off, and answers the new file; null when the name was taken and
    /// <see cref="SfeUploadOptions.Conflicts"/> said Skip. Cancelled, the upload is removed from the server; failed,
    /// it is kept there for a later call to go on with (<see cref="SfeUploadOptions.Resume"/>).
    /// </summary>
    /// <param name="path">The new file's path.</param>
    /// <param name="content">Its bytes, from the stream's position.</param>
    /// <param name="options">Name clashes, size, date, progress; none: the defaults.</param>
    /// <param name="cancellationToken">Stops the upload.</param>
    public Task<SfeItem?> UploadAsync(
        string path,
        Stream content,
        SfeUploadOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var normalized = SfePath.Normalize(path);
        var target = SfePath.Parent(normalized);
        var name = SfePath.Name(normalized);
        return _uploads.UploadAsync(target, name, content, options ?? new SfeUploadOptions(), cancellationToken);
    }

    /// <summary>
    /// Uploads a folder of this computer, with everything in it, into a folder of the library (as a folder of its
    /// name there), and answers the files made. Its folders are made on the way; empty ones are not.
    /// </summary>
    /// <param name="localFolder">The folder on this computer.</param>
    /// <param name="targetFolder">The library's folder it goes into.</param>
    /// <param name="options">Name clashes and progress (of all the files); none: the defaults.</param>
    /// <param name="cancellationToken">Stops the upload (the files already made stay).</param>
    public Task<IReadOnlyList<SfeItem>> UploadFolderAsync(
        string localFolder,
        string targetFolder,
        SfeUploadOptions? options = null,
        CancellationToken cancellationToken = default
    ) =>
        _uploads.UploadFolderAsync(
            localFolder,
            SfePath.Normalize(targetFolder),
            options ?? new SfeUploadOptions(),
            cancellationToken
        );

    /// <summary>Makes the folder <paramref name="path"/> (its parent must exist) and answers it.</summary>
    /// <param name="path">The new folder's path.</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    /// <exception cref="SfeConflictException"><c>file-exists</c>: something of that name is there.</exception>
    public Task<SfeItem> CreateFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        var body = new NameRequest(SfePath.Parent(path), SfePath.Name(path));
        return _transport.ReadAsync<SfeItem>(SfeRequest.Post(Routes.V1("files/folders"), body), cancellationToken);
    }

    /// <summary>
    /// Renames a file or folder in its folder, and answers it; tags, folder rules and links follow it.
    /// </summary>
    /// <param name="path">Its path.</param>
    /// <param name="newName">Its new name (a name, not a path).</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    /// <exception cref="SfeConflictException"><c>file-exists</c>: the name is taken.</exception>
    public Task<SfeItem> RenameAsync(string path, string newName, CancellationToken cancellationToken = default)
    {
        var body = new NameRequest(SfePath.Normalize(path), newName);
        return _transport.ReadAsync<SfeItem>(SfeRequest.Post(Routes.V1("files/rename"), body), cancellationToken);
    }

    /// <summary>
    /// Starts moving files and folders into a folder, and answers the job (<see cref="SfeJob.WaitAsync"/>).
    /// </summary>
    /// <param name="paths">What to move.</param>
    /// <param name="targetFolder">Where to.</param>
    /// <param name="options">Name clashes and folder access; none: the defaults (clashes asked).</param>
    /// <param name="cancellationToken">Stops waiting for the server (not the job).</param>
    public Task<SfeJob> MoveAsync(
        IEnumerable<string> paths,
        string targetFolder,
        SfeJobOptions? options = null,
        CancellationToken cancellationToken = default
    ) => StartJobAsync(SfeJobKind.Move, paths, targetFolder, options, cancellationToken);

    /// <summary>
    /// Starts copying files and folders into a folder, and answers the job (<see cref="SfeJob.WaitAsync"/>).
    /// </summary>
    /// <param name="paths">What to copy.</param>
    /// <param name="targetFolder">Where to.</param>
    /// <param name="options">Name clashes and tags; none: the defaults (clashes asked, tags not copied).</param>
    /// <param name="cancellationToken">Stops waiting for the server (not the job).</param>
    public Task<SfeJob> CopyAsync(
        IEnumerable<string> paths,
        string targetFolder,
        SfeJobOptions? options = null,
        CancellationToken cancellationToken = default
    ) => StartJobAsync(SfeJobKind.Copy, paths, targetFolder, options, cancellationToken);

    /// <summary>Moves files and folders to the trash, and answers what became of each.</summary>
    /// <param name="paths">What to delete.</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    public Task<IReadOnlyList<SfeDeleteResult>> DeleteAsync(
        IEnumerable<string> paths,
        CancellationToken cancellationToken = default
    ) => DeleteAsync(paths, permanent: false, cancellationToken);

    /// <summary>
    /// Deletes files and folders for good, without the trash (needs the right <c>files.deletePermanently</c>), and
    /// answers what became of each.
    /// </summary>
    /// <param name="paths">What to delete.</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    public Task<IReadOnlyList<SfeDeleteResult>> DeletePermanentlyAsync(
        IEnumerable<string> paths,
        CancellationToken cancellationToken = default
    ) => DeleteAsync(paths, permanent: true, cancellationToken);

    private async Task<IReadOnlyList<SfeDeleteResult>> DeleteAsync(
        IEnumerable<string> paths,
        bool permanent,
        CancellationToken cancellationToken
    )
    {
        var results = new List<SfeDeleteResult>();
        var all = paths.Select(SfePath.Normalize).ToList();
        for (var first = 0; first < all.Count; first += DeleteBatch)
        {
            var batch = new DeleteRequest(all.Skip(first).Take(DeleteBatch).ToList(), permanent);
            var answer = await _transport
                .ReadAsync<DeleteAnswer>(SfeRequest.Post(Routes.V1("files/delete"), batch), cancellationToken)
                .ConfigureAwait(false);
            results.AddRange(answer.Items);
        }
        return results;
    }

    private async Task<SfeJob> StartJobAsync(
        SfeJobKind kind,
        IEnumerable<string> paths,
        string targetFolder,
        SfeJobOptions? options,
        CancellationToken cancellationToken
    )
    {
        options ??= new SfeJobOptions();
        var body = new JobRequest(
            kind,
            [.. paths.Select(SfePath.Normalize)],
            SfePath.Normalize(targetFolder),
            options.Conflicts,
            options.KeepAccess,
            options.KeepTags
        );
        var status = await _transport
            .ReadAsync<SfeJobStatus>(SfeRequest.Post(Routes.V1("files/jobs"), body), cancellationToken)
            .ConfigureAwait(false);
        return new SfeJob(_transport, status);
    }

    private static string PageRoute(string folder, int offset, SfeListOptions options)
    {
        var sort = options.Sort.ToString().ToLowerInvariant();
        var descending = options.Descending ? "true" : "false";
        var paging = string.Format(CultureInfo.InvariantCulture, "&offset={0}&limit={1}", offset, options.PageSize);
        return Routes.WithPath("browse", folder) + $"{paging}&sort={sort}&desc={descending}";
    }
}
