using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Otools.Net.Sfe.Client.Connections;
using Otools.Net.Sfe.Client.Http;

namespace Otools.Net.Sfe.Client;

/// <summary>
/// Searches of a connection's files in the library's index of it, as <see cref="SfeSearch"/> has the library's: the
/// same language, sorts and cursors, paths relative to the connection's root, no call to the provider. The index holds
/// what the provider tells when listing (sizes and dates; widths and lengths rarely), and only once the connection has
/// been synced in the library: each page says how much it holds (<see cref="SfeConnectionSearchPage.Coverage"/>).
/// </summary>
/// <example>
/// <code>
/// var photos = sfe.Connections[3].Search;
/// await foreach (var file in photos.QueryAsync("e:jpg s:&gt;2mb"))
///     Console.WriteLine(file.Path);
/// </code>
/// </example>
public sealed class SfeConnectionSearch
{
    private readonly SfeTransport _transport;
    private readonly SfeConnection _connection;

    internal SfeConnectionSearch(SfeTransport transport, SfeConnection connection)
    {
        _transport = transport;
        _connection = connection;
    }

    /// <summary>
    /// The files <paramref name="query"/> finds in the connection, by path; fetched a page at a time. Nothing is found
    /// before its first sync in the library ends (the first search starts it).
    /// </summary>
    /// <param name="query">The search, as typed in the app (<c>""</c>: every indexed file).</param>
    /// <param name="cancellationToken">Stops the search.</param>
    /// <exception cref="SfeNotFoundException">The caller may not use the connection.</exception>
    public IAsyncEnumerable<SfeItem> QueryAsync(string query, CancellationToken cancellationToken = default) =>
        QueryAsync(query, null, cancellationToken);

    /// <summary>
    /// The files <paramref name="query"/> finds, in the folder and order asked; fetched a page at a time as they are
    /// read, each page following the last one's cursor.
    /// </summary>
    /// <param name="query">The search, as typed in the app (<c>""</c>: every indexed file).</param>
    /// <param name="options">The folder searched (<c>""</c>: the root), the order and the size of the pages.</param>
    /// <param name="cancellationToken">Stops the search.</param>
    /// <exception cref="SfeNotFoundException">
    /// The caller may not use the connection, or doesn't see the folder.
    /// </exception>
    public async IAsyncEnumerable<SfeItem> QueryAsync(
        string query,
        SfeSearchOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        options ??= new SfeSearchOptions();
        string? after = null;
        while (true)
        {
            var page = await GetPageAsync(query, options, after, cancellationToken).ConfigureAwait(false);
            foreach (var item in page.Items)
                yield return item;
            if (!page.HasMore || page.Next is null)
                yield break;
            after = page.Next;
        }
    }

    /// <summary>
    /// One page of the files <paramref name="query"/> finds: the first one (with the count) when
    /// <paramref name="after"/> is null, else the one following that cursor; with the index's coverage.
    /// </summary>
    /// <param name="query">The search, as typed in the app.</param>
    /// <param name="options">The folder searched, the order and the size of the page; the same for every page.</param>
    /// <param name="after">
    /// The <see cref="SfeConnectionSearchPage.Next"/> of the previous page; null for the first.
    /// </param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    /// <exception cref="SfeNotFoundException">
    /// The caller may not use the connection, or doesn't see the folder.
    /// </exception>
    public async Task<SfeConnectionSearchPage> GetPageAsync(
        string query,
        SfeSearchOptions? options = null,
        string? after = null,
        CancellationToken cancellationToken = default
    )
    {
        var route = _connection.Place.SearchRoute(query, options ?? new SfeSearchOptions(), after);
        var answer = await _transport
            .ReadAsync<ConnectionSearchAnswer>(SfeRequest.Get(route), cancellationToken)
            .ConfigureAwait(false);
        var page = answer.Results;
        return new SfeConnectionSearchPage
        {
            Items = page.Items,
            HasMore = page.HasMore,
            Next = page.Next,
            Total = page.Total,
            TotalCapped = page.TotalCapped,
            Coverage = answer.Coverage,
        };
    }

    /// <summary>How many files <paramref name="query"/> finds, counted up to the server's limit.</summary>
    /// <param name="query">The search, as typed in the app.</param>
    /// <param name="folder">The folder searched with its sub-folders; <c>""</c>: the whole connection.</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    /// <exception cref="SfeNotFoundException">
    /// The caller may not use the connection, or doesn't see the folder.
    /// </exception>
    public async Task<SfeConnectionSearchCount> CountAsync(
        string query,
        string folder = "",
        CancellationToken cancellationToken = default
    )
    {
        var options = new SfeSearchOptions { Folder = folder, PageSize = 1 };
        var page = await GetPageAsync(query, options, null, cancellationToken).ConfigureAwait(false);
        return new SfeConnectionSearchCount(page.Total ?? page.Items.Count, page.TotalCapped, page.Coverage);
    }
}
