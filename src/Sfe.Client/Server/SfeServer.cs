using System.Threading;
using System.Threading.Tasks;
using Otools.Net.Sfe.Client.Http;
using Otools.Net.Sfe.Client.Server;

namespace Otools.Net.Sfe.Client;

/// <summary>The server itself: its version and the functions it offers, read once per client.</summary>
public sealed class SfeServer
{
    private readonly SfeTransport _transport;
    private readonly SemaphoreSlim _reading = new(1, 1);
    private SfeServerInfo? _info;

    internal SfeServer(SfeTransport transport)
    {
        _transport = transport;
    }

    /// <summary>
    /// What the server tells about itself and the caller, read on the first call and kept: later calls answer
    /// at once (a failed read is tried again on the next call).
    /// </summary>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    public async Task<SfeServerInfo> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        if (_info is { } known)
            return known;
        await _reading.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _info ??= await ReadInfoAsync(cancellationToken).ConfigureAwait(false);
            return _info;
        }
        finally
        {
            _reading.Release();
        }
    }

    /// <summary>
    /// Fails with <see cref="SfeNotSupportedException"/> when the server doesn't offer <paramref name="function"/> to
    /// the caller.
    /// </summary>
    /// <param name="function">The function a call needs.</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    public async Task RequireAsync(SfeFunction function, CancellationToken cancellationToken = default)
    {
        var info = await GetInfoAsync(cancellationToken).ConfigureAwait(false);
        if (!info.Supports(function))
            throw new SfeNotSupportedException(function, info.Version);
    }

    /// <summary>The configuration as the server answers it now (not kept).</summary>
    internal async Task<SfeServerInfo> ReadInfoAsync(CancellationToken cancellationToken)
    {
        var config = await _transport
            .ReadAsync<ConfigAnswer>(SfeRequest.Get(Routes.V1("config")), cancellationToken)
            .ConfigureAwait(false);
        return new SfeServerInfo(config);
    }
}
