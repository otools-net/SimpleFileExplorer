using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Otools.Net.Sfe.Client.Http;
using Otools.Net.Sfe.Client.Trash;

namespace Otools.Net.Sfe.Client;

/// <summary>
/// The library's trash: what the caller deleted, and what was deleted where the caller may delete.
/// </summary>
public sealed class SfeTrash
{
    private const int PageSize = 200;

    private readonly SfeTransport _transport;

    internal SfeTrash(SfeTransport transport)
    {
        _transport = transport;
    }

    /// <summary>
    /// The items of the trash the caller sees, newest first, fetched a page at a time as they are read.
    /// </summary>
    /// <param name="cancellationToken">Stops the listing.</param>
    public async IAsyncEnumerable<SfeTrashItem> ListAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        int? before = null;
        while (true)
        {
            var query = before is { } next ? $"&before={next.ToString(CultureInfo.InvariantCulture)}" : "";
            var route = Routes.V1($"files/trash?limit={PageSize}{query}");
            var page = await _transport
                .ReadAsync<TrashPage>(SfeRequest.Get(route), cancellationToken)
                .ConfigureAwait(false);
            foreach (var item in page.Items)
                yield return item;
            if (!page.HasMore || page.Next is null)
                yield break;
            before = page.Next;
        }
    }

    /// <summary>How many items of the trash the caller sees.</summary>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    public Task<SfeTrashCount> CountAsync(CancellationToken cancellationToken = default) =>
        _transport.ReadAsync<SfeTrashCount>(SfeRequest.Get(Routes.V1("files/trash/count")), cancellationToken);

    /// <summary>
    /// Puts an item of the trash back where it was (its folder made again if gone, <c> (2)</c> added to a name
    /// taken there), and answers it as listings show it.
    /// </summary>
    /// <param name="id">Its id in the trash.</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    public Task<SfeItem> RestoreAsync(int id, CancellationToken cancellationToken = default) =>
        _transport.ReadAsync<SfeItem>(SfeRequest.Post(Route(id, "/restore")), cancellationToken);

    /// <summary>Deletes an item of the trash for good.</summary>
    /// <param name="id">Its id in the trash.</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    public Task DeleteAsync(int id, CancellationToken cancellationToken = default) =>
        _transport.RunAsync(SfeRequest.Delete(Route(id)), cancellationToken);

    /// <summary>Deletes for good everything of the trash the caller may, and answers how many items went.</summary>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    public async Task<int> EmptyAsync(CancellationToken cancellationToken = default)
    {
        var emptied = await _transport
            .ReadAsync<EmptiedTrash>(SfeRequest.Delete(Routes.V1("files/trash")), cancellationToken)
            .ConfigureAwait(false);
        return emptied.Purged;
    }

    private static string Route(int id, string action = "") =>
        Routes.V1($"files/trash/{id.ToString(CultureInfo.InvariantCulture)}{action}");
}
