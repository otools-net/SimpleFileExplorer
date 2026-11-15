using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Otools.Net.Sfe.Client;

/// <summary>
/// The libraries (spaces) the caller reaches. A client works in one: <see cref="SfeClientOptions.Library"/>, else the
/// token's, else the account's first.
/// </summary>
public sealed class SfeLibraries
{
    private readonly SfeServer _server;

    internal SfeLibraries(SfeServer server)
    {
        _server = server;
    }

    /// <summary>The libraries the caller reaches, as the server tells it now.</summary>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    public async Task<IReadOnlyList<SfeLibrary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var info = await _server.ReadInfoAsync(cancellationToken).ConfigureAwait(false);
        return info.Libraries;
    }

    /// <summary>The library this client's calls work in; null when the caller reaches none.</summary>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    public async Task<SfeLibrary?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var info = await _server.ReadInfoAsync(cancellationToken).ConfigureAwait(false);
        return info.Library;
    }
}
