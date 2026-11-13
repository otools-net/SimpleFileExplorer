namespace Sfe.Api;

/// <summary>
/// The API's addresses: the base every route group is mapped under, the groups the request pipeline tells apart, and
/// the links and locations the server hands out. Nothing else in the server writes an <c>api/</c> path
/// (<c>agents/docs/development/api-versioning.md</c>).
/// </summary>
public static class ApiRoutes
{
    /// <summary>The base of every route, relative to the app's address (links keep the virtual directory).</summary>
    public const string LinkBase = "api";

    /// <summary>The base every route group is mapped under.</summary>
    public const string Base = "/" + LinkBase;

    /// <summary>Swagger UI's pages, served by the UI (not routes), outside the API's versions.</summary>
    public const string Docs = "/api/docs";

    /// <summary>The OpenAPI documents.</summary>
    public const string OpenApi = "/openapi";

    // Anything under /api, whatever its version, Swagger UI included.
    private static readonly PathString Root = "/api";
    private static readonly PathString DocsPath = Docs;
    private static readonly PathString Migration = Base + "/migration";

    // The setup, the installation page, the session and the update page.
    private static readonly PathString[] EntryGroups =
    [
        Base + "/setup",
        Base + "/installation",
        Base + "/session",
        Migration,
    ];

    /// <summary>A path under <c>/api</c>: a route of any version, or Swagger UI.</summary>
    public static bool IsApi(PathString path) => path.StartsWithSegments(Root);

    /// <summary>A path under <c>/api</c> or an OpenAPI document.</summary>
    public static bool IsApiOrDocument(PathString path) => IsApi(path) || path.StartsWithSegments(OpenApi);

    /// <summary>Swagger UI's pages.</summary>
    public static bool IsDocs(PathString path) => path.StartsWithSegments(DocsPath);

    /// <summary>The update page's routes: the only ones answering while the install waits for its update.</summary>
    public static bool IsMigration(PathString path) => path.StartsWithSegments(Migration);

    /// <summary>
    /// The routes that let someone into the server (setup, installation, session, update): they answer before the
    /// setup, to other computers in Local mode, and need no library.
    /// </summary>
    public static bool IsServerEntry(PathString path) => EntryGroups.Any(group => path.StartsWithSegments(group));

    /// <summary>
    /// A link to <paramref name="route"/>, relative to the app's address: <c>api/files/shared/…</c>.
    /// </summary>
    public static string Link(string route) => $"{LinkBase}/{route}";

    /// <summary>An absolute location of <paramref name="route"/>: <c>/api/tags/12</c>.</summary>
    public static string Location(string route) => $"{Base}/{route}";
}
