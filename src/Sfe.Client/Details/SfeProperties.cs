using System;
using System.Threading;
using System.Threading.Tasks;
using Otools.Net.Sfe.Client.Details;
using Otools.Net.Sfe.Client.Http;

namespace Otools.Net.Sfe.Client;

/// <summary>
/// Properties of files and folders: dates and attributes, totals from the index, counts on the disk, checksums.
/// </summary>
public sealed class SfeProperties
{
    private readonly SfeTransport _transport;

    internal SfeProperties(SfeTransport transport)
    {
        _transport = transport;
    }

    /// <summary>A file's or folder's properties (<c>""</c>: the library's root).</summary>
    /// <param name="path">The item's path.</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    /// <exception cref="SfeNotFoundException">It isn't there, or the caller doesn't see it.</exception>
    public Task<SfeItemProperties> GetAsync(string path, CancellationToken cancellationToken = default) =>
        _transport.ReadAsync<SfeItemProperties>(
            SfeRequest.Get(Routes.WithPath("files/properties", SfePath.Normalize(path))),
            cancellationToken
        );

    /// <summary>What the index knows below a folder, at once (files not indexed yet are not counted).</summary>
    /// <param name="folder">The folder's path.</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    public Task<SfeFolderTotals> GetTotalsAsync(string folder, CancellationToken cancellationToken = default) =>
        _transport.ReadAsync<SfeFolderTotals>(
            SfeRequest.Get(Routes.WithPath("files/properties/totals", SfePath.Normalize(folder))),
            cancellationToken
        );

    /// <summary>
    /// Counts a folder's files, folders and bytes on the disk (the server walks it in the background), and answers
    /// the count once done. Cancelling stops the count on the server too.
    /// </summary>
    /// <param name="folder">The folder's path.</param>
    /// <param name="progress">Told the count so far, each time it is asked.</param>
    /// <param name="cancellationToken">Stops the count.</param>
    public async Task<SfeFolderCount> CountAsync(
        string folder,
        IProgress<SfeFolderCount>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var start = SfeRequest.Post(Routes.V1("files/properties/count"), new CountRequest(SfePath.Normalize(folder)));
        var job = await RunAsync(start, job => progress?.Report(job.ToCount()), cancellationToken)
            .ConfigureAwait(false);
        return job.ToCount();
    }

    /// <summary>
    /// Reads a file whole on the server for its checksum, and answers it in lower-case hexadecimal. Cancelling stops
    /// the reading on the server too.
    /// </summary>
    /// <param name="path">The file's path.</param>
    /// <param name="algorithm">The algorithm.</param>
    /// <param name="progress">Told the bytes read so far, each time it is asked.</param>
    /// <param name="cancellationToken">Stops the reading.</param>
    public async Task<string> ChecksumAsync(
        string path,
        SfeChecksumAlgorithm algorithm = SfeChecksumAlgorithm.Sha256,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var body = new ChecksumRequest(SfePath.Normalize(path), algorithm);
        var start = SfeRequest.Post(Routes.V1("files/properties/checksum"), body);
        var job = await RunAsync(start, job => progress?.Report(job.Bytes), cancellationToken).ConfigureAwait(false);
        return job.Checksum ?? throw new SfeException("The server finished the checksum without giving it.");
    }

    /// <summary>Starts a count or checksum and follows it to its end; cancelled, it is stopped on the server.</summary>
    private async Task<PropertyJob> RunAsync(
        SfeRequest start,
        Action<PropertyJob> report,
        CancellationToken cancellationToken
    )
    {
        var job = await _transport.ReadAsync<PropertyJob>(start, cancellationToken).ConfigureAwait(false);
        var route = Routes.V1($"files/properties/jobs/{job.Id}");
        try
        {
            job = await FollowAsync(job, route, report, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await StopAsync(route).ConfigureAwait(false);
            throw;
        }
        if (job.Error is { } error)
            throw new SfeException(error);
        return job;
    }

    private async Task<PropertyJob> FollowAsync(
        PropertyJob job,
        string route,
        Action<PropertyJob> report,
        CancellationToken cancellationToken
    )
    {
        report(job);
        while (!job.Done)
        {
            await Task.Delay(_transport.Options.PollInterval, cancellationToken).ConfigureAwait(false);
            job = await _transport
                .ReadAsync<PropertyJob>(SfeRequest.Get(route), cancellationToken)
                .ConfigureAwait(false);
            report(job);
        }
        return job;
    }

    /// <summary>Stops a job the caller no longer waits for: best effort, it ends by itself anyway.</summary>
    private async Task StopAsync(string route)
    {
        try
        {
            // Not the caller's token: it is cancelled already, and the stop must still be sent.
            await _transport.RunAsync(SfeRequest.Delete(route), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exception is SfeException or System.Net.Http.HttpRequestException or TimeoutException)
        {
            // The job may have ended meanwhile (404), or the server be gone: nothing left to stop.
        }
    }
}
