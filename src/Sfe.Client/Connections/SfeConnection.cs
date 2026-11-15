using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Otools.Net.Sfe.Client.Connections;
using Otools.Net.Sfe.Client.Http;

namespace Otools.Net.Sfe.Client;

/// <summary>
/// One connection: its files (<see cref="Files"/>), what its provider can do, and moves and copies between it and
/// the library or another connection, as jobs. Paths are relative to the connection's root folder, with <c>/</c>.
/// </summary>
public sealed class SfeConnection
{
    private readonly SfeTransport _transport;
    private readonly SfeConnections _connections;
    private SfeConnectionCapabilities? _capabilities;

    internal SfeConnection(SfeTransport transport, SfeConnections connections, int id)
    {
        _transport = transport;
        _connections = connections;
        Id = id;
        Place = FilePlace.Connection(id);
        Files = new SfeConnectionFiles(transport, this);
    }

    /// <summary>The connection's id.</summary>
    public int Id { get; }

    /// <summary>Its files and folders: listed, read, uploaded, made, renamed, moved, copied, deleted.</summary>
    public SfeConnectionFiles Files { get; }

    /// <summary>Where its file routes go.</summary>
    internal FilePlace Place { get; }

    /// <summary>The connection as <see cref="SfeConnections.ListAsync"/> tells it now.</summary>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    /// <exception cref="SfeNotFoundException">The caller may not use it (or it isn't there).</exception>
    public async Task<SfeConnectionInfo> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        var usable = await _connections.ListAsync(cancellationToken).ConfigureAwait(false);
        return usable.FirstOrDefault(connection => connection.Id == Id)
            ?? throw new SfeNotFoundException(404, new SfeProblem { Detail = $"No connection {Id} to use." });
    }

    /// <summary>
    /// What its provider can do, read on the first call and kept (from the server's configuration, else from a
    /// listing of the root folder).
    /// </summary>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    /// <exception cref="SfeNotFoundException">The caller may not use it (or it isn't there).</exception>
    public async Task<SfeConnectionCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        if (_capabilities is { } known)
            return known;
        var info = await GetInfoAsync(cancellationToken).ConfigureAwait(false);
        _capabilities = info.Capabilities ?? await ReadCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        return _capabilities;
    }

    /// <summary>
    /// Starts copying files and folders of the library into a folder of this connection, and answers the job
    /// (<see cref="SfeJob.WaitAsync"/>; <see cref="SfeJob.RetryAsync"/> once it ended with failures).
    /// </summary>
    /// <param name="libraryPaths">What to copy, in the library.</param>
    /// <param name="targetFolder">The connection's folder they go into.</param>
    /// <param name="options">Name clashes (<see cref="SfeJobOptions.Conflicts"/> only); none: asked.</param>
    /// <param name="cancellationToken">Stops waiting for the server (not the job).</param>
    public Task<SfeJob> CopyFromLibraryAsync(
        IEnumerable<string> libraryPaths,
        string targetFolder,
        SfeJobOptions? options = null,
        CancellationToken cancellationToken = default
    ) => StartAsync(SfeJobKind.Copy, Side(null, libraryPaths), Target(Id, targetFolder), options, cancellationToken);

    /// <summary>
    /// Starts moving files and folders of the library into a folder of this connection (each source goes to the
    /// library's trash once its copy is verified), and answers the job.
    /// </summary>
    /// <param name="libraryPaths">What to move, in the library.</param>
    /// <param name="targetFolder">The connection's folder they go into.</param>
    /// <param name="options">Name clashes (<see cref="SfeJobOptions.Conflicts"/> only); none: asked.</param>
    /// <param name="cancellationToken">Stops waiting for the server (not the job).</param>
    public Task<SfeJob> MoveFromLibraryAsync(
        IEnumerable<string> libraryPaths,
        string targetFolder,
        SfeJobOptions? options = null,
        CancellationToken cancellationToken = default
    ) => StartAsync(SfeJobKind.Move, Side(null, libraryPaths), Target(Id, targetFolder), options, cancellationToken);

    /// <summary>Starts copying files and folders of this connection into a folder of the library.</summary>
    /// <param name="paths">What to copy, in this connection.</param>
    /// <param name="libraryFolder">The library's folder they go into.</param>
    /// <param name="options">Name clashes (<see cref="SfeJobOptions.Conflicts"/> only); none: asked.</param>
    /// <param name="cancellationToken">Stops waiting for the server (not the job).</param>
    public Task<SfeJob> CopyToLibraryAsync(
        IEnumerable<string> paths,
        string libraryFolder,
        SfeJobOptions? options = null,
        CancellationToken cancellationToken = default
    ) => StartAsync(SfeJobKind.Copy, Side(Id, paths), Target(null, libraryFolder), options, cancellationToken);

    /// <summary>
    /// Starts moving files and folders of this connection into a folder of the library (each source goes to the
    /// provider's trash once its copy is verified), and answers the job.
    /// </summary>
    /// <param name="paths">What to move, in this connection.</param>
    /// <param name="libraryFolder">The library's folder they go into.</param>
    /// <param name="options">Name clashes (<see cref="SfeJobOptions.Conflicts"/> only); none: asked.</param>
    /// <param name="cancellationToken">Stops waiting for the server (not the job).</param>
    public Task<SfeJob> MoveToLibraryAsync(
        IEnumerable<string> paths,
        string libraryFolder,
        SfeJobOptions? options = null,
        CancellationToken cancellationToken = default
    ) => StartAsync(SfeJobKind.Move, Side(Id, paths), Target(null, libraryFolder), options, cancellationToken);

    /// <summary>
    /// Starts copying files and folders of this connection into a folder of another one (the bytes go through the
    /// server), and answers the job. Inside this connection: <see cref="SfeConnectionFiles.CopyAsync"/>.
    /// </summary>
    /// <param name="paths">What to copy, in this connection.</param>
    /// <param name="target">The other connection.</param>
    /// <param name="targetFolder">Its folder they go into.</param>
    /// <param name="options">Name clashes (<see cref="SfeJobOptions.Conflicts"/> only); none: asked.</param>
    /// <param name="cancellationToken">Stops waiting for the server (not the job).</param>
    public Task<SfeJob> CopyToAsync(
        IEnumerable<string> paths,
        SfeConnection target,
        string targetFolder,
        SfeJobOptions? options = null,
        CancellationToken cancellationToken = default
    ) => StartAsync(SfeJobKind.Copy, Side(Id, paths), Target(target.Id, targetFolder), options, cancellationToken);

    /// <summary>
    /// Starts moving files and folders of this connection into a folder of another one, and answers the job. Inside
    /// this connection: <see cref="SfeConnectionFiles.MoveAsync"/>.
    /// </summary>
    /// <param name="paths">What to move, in this connection.</param>
    /// <param name="target">The other connection.</param>
    /// <param name="targetFolder">Its folder they go into.</param>
    /// <param name="options">Name clashes (<see cref="SfeJobOptions.Conflicts"/> only); none: asked.</param>
    /// <param name="cancellationToken">Stops waiting for the server (not the job).</param>
    public Task<SfeJob> MoveToAsync(
        IEnumerable<string> paths,
        SfeConnection target,
        string targetFolder,
        SfeJobOptions? options = null,
        CancellationToken cancellationToken = default
    ) => StartAsync(SfeJobKind.Move, Side(Id, paths), Target(target.Id, targetFolder), options, cancellationToken);

    /// <summary>
    /// Fails with <see cref="SfeNotSupportedException"/> when the provider can't do <paramref name="capability"/>.
    /// </summary>
    internal async Task RequireAsync(SfeConnectionCapability capability, CancellationToken cancellationToken)
    {
        var capabilities = await GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        if (!capabilities.Has(capability))
            throw new SfeNotSupportedException(capability, Id);
    }

    /// <summary>Keeps the capabilities a listing told (the server's own word for this connection).</summary>
    internal void Learn(SfeConnectionCapabilities capabilities) => _capabilities = capabilities;

    private async Task<SfeConnectionCapabilities> ReadCapabilitiesAsync(CancellationToken cancellationToken)
    {
        var route = Place.PageRoute("", 0, new SfeListOptions { PageSize = 1 });
        var page = await _transport
            .ReadAsync<ConnectionPage>(SfeRequest.Get(route), cancellationToken)
            .ConfigureAwait(false);
        return page.Capabilities;
    }

    private async Task<SfeJob> StartAsync(
        SfeJobKind kind,
        TransferSide from,
        TransferTarget to,
        SfeJobOptions? options,
        CancellationToken cancellationToken
    )
    {
        var body = new TransferRequest(kind, from, to, (options ?? new SfeJobOptions()).Conflicts);
        var status = await _transport
            .ReadAsync<SfeJobStatus>(SfeRequest.Post(Routes.V1("files/jobs/transfer"), body), cancellationToken)
            .ConfigureAwait(false);
        return new SfeJob(_transport, status);
    }

    private static TransferSide Side(int? connection, IEnumerable<string> paths) =>
        new(connection, [.. paths.Select(SfePath.Normalize)]);

    private static TransferTarget Target(int? connection, string folder) => new(connection, SfePath.Normalize(folder));
}
