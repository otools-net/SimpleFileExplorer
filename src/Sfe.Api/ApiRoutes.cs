namespace Sfe.Api;

/// <summary>
/// The API's addresses: the base every route group is mapped under, the groups the request pipeline tells apart, and
/// the links and locations the server hands out. Nothing else in the server writes an <c>api/</c> path
/// (<c>agents/docs/development/api-versioning.md</c>).
/// </summary>
public static class ApiRoutes
{
    /// <summary>The base of every route, relative to the app's address (links keep the virtual directory).</summary>
    public const string LinkBase = "api/v1";

    /// <summary>The base every route group is mapped under.</summary>
    public const string Base = "/" + LinkBase;

    /// <summary>Swagger UI's pages, served by the UI (not routes), outside the API's versions.</summary>
    public const string Docs = "/api/docs";

    /// <summary>The OpenAPI documents.</summary>
    public const string OpenApi = "/openapi";

    /// <summary>
    /// Where a provider sends the browser back after an OAuth sign-in: registered at the provider by administrators
    /// with this unversioned path, so it keeps it for good (answered by v1 through <see cref="LegacyApiPaths"/>,
    /// without the deprecation).
    /// </summary>
    public const string OAuthCallback = "/api/connections/oauth/callback";

    /// <summary>The unversioned base: another name of v1 until SFE 2.0 (<see cref="LegacyApiPaths"/>).</summary>
    public const string LegacyBase = "/api";

    // Anything under /api, whatever its version, Swagger UI included.
    private static readonly PathString Root = LegacyBase;
    private static readonly PathString BasePath = Base;
    private static readonly PathString OAuthCallbackPath = OAuthCallback;
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

    /// <summary>
    /// An unversioned path of a v1 route (<c>/api/tags</c>): not <see cref="Base"/>, nor Swagger UI's pages, nor
    /// another version's.
    /// </summary>
    public static bool IsLegacy(PathString path) =>
        path.StartsWithSegments(Root, out var rest)
        && rest.HasValue
        && !path.StartsWithSegments(BasePath)
        && !IsDocs(path)
        && !IsVersionSegment(rest.Value!);

    /// <summary>
    /// The v1 path of an unversioned one (<see cref="IsLegacy"/>): <c>/api/tags</c> is <c>/api/v1/tags</c>.
    /// </summary>
    public static PathString ToVersioned(PathString legacy) => BasePath.Add(legacy.Value![LegacyBase.Length..]);

    /// <summary>The OAuth return address (<see cref="OAuthCallback"/>): unversioned for good.</summary>
    public static bool IsOAuthCallback(PathString path) => path.Equals(OAuthCallbackPath);

    // "/v2/…": a later version's path, never an alias.
    private static bool IsVersionSegment(string rest)
    {
        var segment = rest[1..].Split('/')[0];
        return segment.Length > 1 && segment[0] == 'v' && segment[1..].All(char.IsAsciiDigit);
    }

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
