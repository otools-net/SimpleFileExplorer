namespace Otools.Net.Sfe.Api;

/// <summary>
/// Every <c>type</c> word of the API's problems (ProblemDetails): the word is the contract that clients test, the text
/// is for people. A word never changes for the same case inside a version; a new case gets a new word, added here,
/// to <see cref="All"/>, and to the lists of agents/docs/development/api-versioning.md and docs/reference/api.md
/// (<c>ProblemTypesTests</c> checks all three). A problem without a word of its own carries the default of
/// ASP.NET Core (a link to its HTTP status in RFC 9110).
/// </summary>
public static class ProblemTypes
{
    /// <summary>409: a move would let more people see or change a folder; confirm first.</summary>
    public const string AccessGains = "access-gains";

    /// <summary>409: the archive's list is still being read; try again in a moment.</summary>
    public const string ArchiveReading = "archive-reading";

    /// <summary>422: the archive (or one of its entries) can't be read: damaged, or of a kind not read.</summary>
    public const string ArchiveUnreadable = "archive-unreadable";

    /// <summary>409: the provider no longer accepts the connection's sign-in; its manager signs in again.</summary>
    public const string ConnectionNeedsReconnecting = "connection-needs-reconnecting";

    /// <summary>409: the connection's provider lacks the function.</summary>
    public const string ConnectionNotSupported = "connection-not-supported";

    /// <summary>429 with <c>Retry-After</c>: the connection's provider asks to wait.</summary>
    public const string ConnectionRateLimited = "connection-rate-limited";

    /// <summary>403: the connection's provider does not let this account do it.</summary>
    public const string ConnectionRefused = "connection-refused";

    /// <summary>502: the connection's provider can't be reached, or failed on its side; try later.</summary>
    public const string ConnectionUnavailable = "connection-unavailable";

    /// <summary>409: the connection is off, or its kind or level is no longer allowed.</summary>
    public const string ConnectionUnusable = "connection-unusable";

    /// <summary>412: the text file changed since it was read (its current version in <c>version</c>).</summary>
    public const string FileChanged = "file-changed";

    /// <summary>409: something of that name is already there.</summary>
    public const string FileExists = "file-exists";

    /// <summary>409: the file is a link; it is not saved over.</summary>
    public const string FileIsLink = "file-is-link";

    /// <summary>409: the folder access rules can't follow a folder there (rules are set for that place).</summary>
    public const string FolderRules = "folder-rules";

    /// <summary>409: a retry asked while the job still runs.</summary>
    public const string JobRunning = "job-running";

    /// <summary>404: the library's folder doesn't exist on the server (moved drive, renamed folder).</summary>
    public const string LibraryRootMissing = "library-root-missing";

    /// <summary>400: a sign-in refused because a Local server lets only administrators in.</summary>
    public const string LocalAdministratorsOnly = "local-administrators-only";

    /// <summary>403: a Local server answers its own computer only.</summary>
    public const string LocalOnly = "local-only";

    /// <summary>503: the server waits for its update to be started from the app.</summary>
    public const string MigrationRequired = "migration-required";

    /// <summary>422: some characters can't be written in the file's encoding.</summary>
    public const string NotEncodable = "not-encodable";

    /// <summary>409: the file can't be explored as an archive (extract it instead).</summary>
    public const string NotExplorable = "not-explorable";

    /// <summary>409: nothing in the archive is protected by a password.</summary>
    public const string NotProtected = "not-protected";

    /// <summary>415: the file is not text.</summary>
    public const string NotText = "not-text";

    /// <summary>409: a retry of a job with nothing left to start again.</summary>
    public const string NothingToRetry = "nothing-to-retry";

    /// <summary>409: the file is read-only; it is not saved over.</summary>
    public const string ReadOnly = "read-only";

    /// <summary>409: a scan of the library runs; try again once it ends.</summary>
    public const string ScanRunning = "scan-running";

    /// <summary>403: a route for signed-in sessions only, called with an access token.</summary>
    public const string SessionOnly = "session-only";

    /// <summary>503: the server has no administrator yet; it is set up from the app.</summary>
    public const string SetupRequired = "setup-required";

    /// <summary>409: the session is in another space now; reload.</summary>
    public const string TenantChanged = "tenant-changed";

    /// <summary>413: too large to be shown or saved as text, or to be read from an archive.</summary>
    public const string TooLarge = "too-large";

    /// <summary>428: a save over a file without the version it replaces (<c>If-Match</c>).</summary>
    public const string VersionRequired = "version-required";

    /// <summary>403 or 422: the archive needs a password, or the one given doesn't open it.</summary>
    public const string WrongPassword = "wrong-password";

    /// <summary>Every word, in alphabetical order (also in the OpenAPI document, <c>x-sfe-problem-types</c>).</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        AccessGains,
        ArchiveReading,
        ArchiveUnreadable,
        ConnectionNeedsReconnecting,
        ConnectionNotSupported,
        ConnectionRateLimited,
        ConnectionRefused,
        ConnectionUnavailable,
        ConnectionUnusable,
        FileChanged,
        FileExists,
        FileIsLink,
        FolderRules,
        JobRunning,
        LibraryRootMissing,
        LocalAdministratorsOnly,
        LocalOnly,
        MigrationRequired,
        NotEncodable,
        NotExplorable,
        NotProtected,
        NotText,
        NothingToRetry,
        ReadOnly,
        ScanRunning,
        SessionOnly,
        SetupRequired,
        TenantChanged,
        TooLarge,
        VersionRequired,
        WrongPassword,
    ];
}
