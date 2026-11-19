using System.Globalization;

namespace Otools.Net.Sfe.Client.Http;

/// <summary>
/// Where file routes go: the library's (<c>api/v1/files/…</c>) or a connection's
/// (<c>api/v1/connections/{id}/files/…</c>, the same routes below its base), so that one copy of the downloads,
/// uploads and listings serves both.
/// </summary>
internal sealed class FilePlace
{
    private readonly string _prefix;

    private FilePlace(int? connectionId)
    {
        ConnectionId = connectionId;
        _prefix = connectionId is { } id ? $"connections/{id.ToString(CultureInfo.InvariantCulture)}/" : "";
    }

    /// <summary>The library of the client.</summary>
    public static FilePlace Library { get; } = new(null);

    /// <summary>The connection <paramref name="id"/>.</summary>
    public static FilePlace Connection(int id) => new(id);

    /// <summary>The connection's id; null for the library.</summary>
    public int? ConnectionId { get; }

    /// <summary><c>api/v1/{place}{route}</c>.</summary>
    public string Route(string route) => Routes.V1(_prefix + route);

    /// <summary><c>api/v1/{place}{route}?path=…</c>.</summary>
    public string WithPath(string route, string path) => Routes.WithPath(_prefix + route, path);

    /// <summary><c>api/v1/{place}{route}/a/b%20c</c>.</summary>
    public string WithPathSegments(string route, string path) => Routes.WithPathSegments(_prefix + route, path);

    /// <summary>A page of a folder (<c>browse</c>) from <paramref name="offset"/>, in the order asked.</summary>
    public string PageRoute(string folder, int offset, SfeListOptions options)
    {
        var sort = options.Sort.ToString().ToLowerInvariant();
        var descending = options.Descending ? "true" : "false";
        var paging = string.Format(CultureInfo.InvariantCulture, "&offset={0}&limit={1}", offset, options.PageSize);
        return WithPath("browse", folder) + $"{paging}&sort={sort}&desc={descending}";
    }

    /// <summary>
    /// A page of a search (<c>search</c>) of <paramref name="query"/>: the first one when <paramref name="after"/> is
    /// null, else the one following that cursor.
    /// </summary>
    public string SearchRoute(string query, SfeSearchOptions options, string? after)
    {
        var sort = options.Sort.ToString().ToLowerInvariant();
        var descending = options.Descending ? "true" : "false";
        var limit = options.PageSize.ToString(CultureInfo.InvariantCulture);
        var route = WithPath("search", SfePath.Normalize(options.Folder));
        route += $"&q={Routes.Escape(query ?? "")}&limit={limit}&sort={sort}&desc={descending}";
        return after is null ? route : route + $"&after={Routes.Escape(after)}";
    }
}
