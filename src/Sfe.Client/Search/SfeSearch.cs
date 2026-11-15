using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Otools.Net.Sfe.Client.Http;

namespace Otools.Net.Sfe.Client;

/// <summary>
/// Searches of the library's index, in the app's search language: words, <c>e:mp4</c>, <c>s:&gt;1gb</c>,
/// <c>t:tag</c>, <c>-term</c>… (<c>docs/reference/search-syntax.md</c>). Only indexed files are found.
/// </summary>
/// <example>
/// <code>
/// await foreach (var file in sfe.Search.QueryAsync("holiday e:mp4,mkv -t:archived"))
///     Console.WriteLine(file.Path);
/// </code>
/// </example>
public sealed class SfeSearch
{
    private readonly SfeTransport _transport;

    internal SfeSearch(SfeTransport transport)
    {
        _transport = transport;
    }

    /// <summary>The files <paramref name="query"/> finds in the library, by path; fetched a page at a time.</summary>
    /// <param name="query">The search, as typed in the app (<c>""</c>: every indexed file).</param>
    /// <param name="cancellationToken">Stops the search.</param>
    public IAsyncEnumerable<SfeItem> QueryAsync(string query, CancellationToken cancellationToken = default) =>
        QueryAsync(query, null, cancellationToken);

    /// <summary>
    /// The files <paramref name="query"/> finds, in the folder and order asked; fetched a page at a time as they are
    /// read, each page following the last one's cursor (so files added meanwhile don't shift the results).
    /// </summary>
    /// <param name="query">The search, as typed in the app (<c>""</c>: every indexed file).</param>
    /// <param name="options">The folder searched, the order and the size of the pages.</param>
    /// <param name="cancellationToken">Stops the search.</param>
    /// <exception cref="SfeNotFoundException">The caller doesn't see the folder.</exception>
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
    /// <paramref name="after"/> is null, else the one following that cursor. For a program that pages itself.
    /// </summary>
    /// <param name="query">The search, as typed in the app.</param>
    /// <param name="options">The folder searched, the order and the size of the page; the same for every page.</param>
    /// <param name="after">The <see cref="SfeSearchPage.Next"/> of the previous page; null for the first.</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    /// <exception cref="SfeNotFoundException">The caller doesn't see the folder.</exception>
    public Task<SfeSearchPage> GetPageAsync(
        string query,
        SfeSearchOptions? options = null,
        string? after = null,
        CancellationToken cancellationToken = default
    )
    {
        var route = PageRoute(query, options ?? new SfeSearchOptions(), after);
        return _transport.ReadAsync<SfeSearchPage>(SfeRequest.Get(route), cancellationToken);
    }

    /// <summary>How many files <paramref name="query"/> finds, counted up to the server's limit.</summary>
    /// <param name="query">The search, as typed in the app.</param>
    /// <param name="folder">The folder searched with its sub-folders; <c>""</c>: the whole library.</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    /// <exception cref="SfeNotFoundException">The caller doesn't see the folder.</exception>
    public async Task<SfeSearchCount> CountAsync(
        string query,
        string folder = "",
        CancellationToken cancellationToken = default
    )
    {
        var options = new SfeSearchOptions { Folder = folder, PageSize = 1 };
        var page = await GetPageAsync(query, options, null, cancellationToken).ConfigureAwait(false);
        return new SfeSearchCount(page.Total ?? page.Items.Count, page.TotalCapped);
    }

    private static string PageRoute(string query, SfeSearchOptions options, string? after)
    {
        var sort = options.Sort.ToString().ToLowerInvariant();
        var descending = options.Descending ? "true" : "false";
        var limit = options.PageSize.ToString(CultureInfo.InvariantCulture);
        var route = Routes.WithPath("search", SfePath.Normalize(options.Folder));
        route += $"&q={Routes.Escape(query ?? "")}&limit={limit}&sort={sort}&desc={descending}";
        return after is null ? route : route + $"&after={Routes.Escape(after)}";
    }
}
