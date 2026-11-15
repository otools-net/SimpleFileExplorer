using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Connections.Indexing;
using Otools.Net.Sfe.Core.Tagging;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// The library's tags on a connection's files (<c>api/connections/{id}/file-tags…</c>): the routes of
/// <c>api/file-tags</c> with the connection's id in front, on the rows of the library's index of it
/// (<see cref="RemoteFileTags"/>, <see cref="RemoteScopeTagger"/>). Opened as every route on a connection's files
/// (<see cref="ConnectionFileServices.OpenAsync(int, string, CancellationToken)"/>): 404 for a connection the session
/// may not use (an account's own is its owner's only, never its library's administrators'), then the tag right at
/// each path by the connection's folder rules (hidden 404, seen but refused 403 or the change refused).
/// </summary>
public static class ConnectionTagEndpoints
{
    private const string Scope = "; 404 for a connection the subject may not use or a path it doesn't see";

    private const string GetDescription = """
        The tags of a file of the connection, from the library's index (no provider call); a file the index doesn't
        hold yet has none.
        """;

    private static readonly string ChangeDescription = $"""
        As `POST api/file-tags`, on files of the connection (paths relative to its root, at most
        {TagService.MaxFilesPerChange}). A file the library's index doesn't hold yet is asked of the provider and
        indexed first (at most {ConnectionTagChanges.MaxFetched} such files a request); 409
        `{ProblemTypes.ConnectionNotIndexed}` when the connection is not indexed in this library yet (its first sync
        starts: try again once it ends) or more files than that are missing from its index.
        """;

    private const string ScopeDescription = """
        As `api/file-tags/scope`, on the connection's files the library's index holds (paths relative to its root):
        a file not indexed yet is neither counted nor changed, so a folder is never listed for it. Folder rules apply:
        hidden files are in no count; a change touches only the files where the subject may tag. The files are
        changed by batches: a request that is cancelled keeps the batches already written.
        """;

    public static RouteGroupBuilder MapConnectionTagEndpoints(this RouteGroupBuilder api)
    {
        var tags = api.MapGroup("/connections/{id:int}/file-tags").WithTags("Connections");
        tags.MapGet("/", GetFileTags)
            .RequireRight(Rights.UseConnections)
            .FolderScoped("tags.view at the file" + Scope)
            .Produces<FileTags>()
            .WithName("GetConnectionFileTags")
            .WithSummary("Tags of a file of a connection, also grouped by category tag.")
            .WithDescription(GetDescription);
        tags.MapPost("/", ConnectionTagChanges.ChangeAsync)
            .RequireRateLimiting(RequestLimits.Connections)
            .RequireRight(Rights.UseConnections)
            .FolderScoped("tags.assign at every file, else nothing changes" + Scope)
            .Produces<FileTagChangeResult>()
            .WithName("ChangeConnectionFileTags")
            .WithSummary("Adds and removes tags on one or more files of a connection.")
            .WithDescription(ChangeDescription);
        tags.MapGet("/scope", GetScopeTags)
            .RequireRight(Rights.UseConnections)
            .FolderScoped("tags.view; only the files the subject sees count" + Scope)
            .Produces<ScopeTags>()
            .WithName("GetConnectionScopeTags")
            .WithSummary("Tags of the indexed files of a connection's folder (or tree), or of a search's matches.")
            .WithDescription(ScopeDescription);
        tags.MapPost("/scope", ChangeScopeTags)
            .RequireRight(Rights.UseConnections)
            .FolderScoped("tags.assign; only the files where the subject may tag change" + Scope)
            .Produces<ScopeTagChangeResult>()
            .WithName("ChangeConnectionScopeTags")
            .WithSummary("Adds and removes tags on every indexed file of a connection's folder, or every match.")
            .WithDescription(ScopeDescription);
        return api;
    }

    private static async Task<IResult> GetFileTags(
        int id,
        string path,
        [AsParameters] ConnectionFileServices files,
        RemoteFileTags tags,
        CancellationToken cancellationToken
    )
    {
        if (RemotePaths.Normalize(path) is not { Length: > 0 } asked)
            return TypedResults.NotFound();
        var (open, answer) = await files.OpenAsync(id, Rights.ViewTags, cancellationToken);
        if (open is null)
            return answer!;
        if (OpenConnection.AnswerOf(open.Check(asked, Rights.ViewTags)) is { } refused)
            return refused;
        if (await PlaceAsync(files, open, cancellationToken) is not { } place)
            return TypedResults.NotFound();
        var rows = await tags.RowsAsync(place, [asked], cancellationToken);
        // A rule set on the file at another path (renamed outside SFE) applies too.
        if (rows.Any(row => !open.Folders.At(row.Path, row.ProviderId).Has(Rights.ViewTags)))
            return TypedResults.NotFound();
        return TypedResults.Ok(await tags.TagsOfAsync(place, asked, cancellationToken));
    }

    private static async Task<IResult> GetScopeTags(
        int id,
        [AsParameters] ConnectionFileServices files,
        RemoteScopeTagger tagger,
        CancellationToken cancellationToken,
        [AsParameters] ConnectionTagScopeQuery query
    )
    {
        var (scope, open, answer) = await ScopeAsync(id, files, (query.Scope(), Rights.ViewTags), cancellationToken);
        if (scope is null)
            return answer!;
        var visible = scope with { Visible = open!.Folders.Where(Rights.ViewTags) };
        return TypedResults.Ok(await tagger.GetTagsAsync(visible, cancellationToken));
    }

    private static async Task<IResult> ChangeScopeTags(
        int id,
        [AsParameters] ConnectionFileServices files,
        RemoteScopeTagger tagger,
        ScopeTagChange change,
        CancellationToken cancellationToken
    )
    {
        var asked = new TagScope(change.Path, change.Recursive, change.Query);
        var (scope, open, answer) = await ScopeAsync(id, files, (asked, Rights.AssignTags), cancellationToken);
        if (scope is null)
            return answer!;
        var where = scope with { Taggable = open!.Folders.Where(Rights.AssignTags) };
        var outcome = await tagger.ChangeAsync(where, change.Add, change.Remove, cancellationToken);
        return outcome.Status == ScopeTagStatus.Ok
            ? TypedResults.Ok(outcome.Result!)
            : TypedResults.ValidationProblem(outcome.Errors!);
    }

    /// <summary>
    /// The connection opened for <paramref name="ask"/>'s right and its scope, the folder seen by the session (with
    /// the rules hiding files by id); or the answer to give.
    /// </summary>
    private static async Task<(RemoteTagScope? Scope, OpenConnection? Open, IResult? Answer)> ScopeAsync(
        int id,
        ConnectionFileServices files,
        (TagScope Scope, string Right) ask,
        CancellationToken cancellationToken
    )
    {
        if (RemotePaths.Normalize(ask.Scope.Path) is not { } path)
            return (null, null, TypedResults.NotFound());
        var (open, answer) = await files.OpenAsync(id, ask.Right, cancellationToken);
        if (open is null)
            return (null, null, answer);
        if (!open.At(path).IsVisible || await TargetAsync(files, open, cancellationToken) is not { } target)
            return (null, null, TypedResults.NotFound());
        var scope = new RemoteTagScope
        {
            Target = target,
            Scope = ask.Scope with { Path = path },
            Visible = open.Folders.Visible(),
            HiddenIds = open.Folders.HiddenIds(),
        };
        return (scope, open, null);
    }

    /// <summary>The connection's rows as its index is made now (root folder, account); null when it is gone.</summary>
    internal static async Task<RemoteTagPlace?> PlaceAsync(
        ConnectionFileServices files,
        OpenConnection open,
        CancellationToken cancellationToken
    ) => await TargetAsync(files, open, cancellationToken) is { } target ? RemoteTagPlace.Of(target) : null;

    private static async Task<ConnectionIndexTarget?> TargetAsync(
        ConnectionFileServices files,
        OpenConnection open,
        CancellationToken cancellationToken
    ) =>
        await files.Connections.FindAsync(open.Connection.Id, cancellationToken) is { } info
            ? ConnectionIndexTarget.Of(info, open.Capabilities)
            : null;
}
