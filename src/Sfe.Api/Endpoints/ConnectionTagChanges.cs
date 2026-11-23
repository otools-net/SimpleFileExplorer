using Microsoft.AspNetCore.Http.HttpResults;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Connections.Indexing;
using Otools.Net.Sfe.Core.Tagging;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// <c>POST api/connections/{id}/file-tags</c>: tags added to and removed from files of a connection. As in the
/// library, a file the subject may not tag fails the whole change (the same answer as a file that isn't there); a
/// file the library's index doesn't hold yet is asked of the provider and its row written (as SFE's own changes write
/// theirs), so that it can carry the tags.
/// </summary>
internal static class ConnectionTagChanges
{
    /// <summary>Files missing from the index asked of the provider in one request.</summary>
    public const int MaxFetched = 20;

    private const string NotFiles = "Some of these files are not files of the connection.";

    public static async Task<IResult> ChangeAsync(
        int id,
        FileTagChange change,
        [AsParameters] ConnectionFileServices files,
        [AsParameters] ConnectionTagServices tags,
        CancellationToken cancellationToken
    )
    {
        if (Invalid(change) is { } invalid)
            return invalid;
        var (open, answer) = await files.OpenAsync(id, Rights.AssignTags, cancellationToken);
        if (open is null)
            return answer!;
        var paths = change.Paths.Select(RemotePaths.Normalize).ToList();
        if (paths.Any(p => p is not { Length: > 0 } path || open.Check(path, Rights.AssignTags) != PathVerdict.Allowed))
            return Problem("paths", NotFiles);
        if (await ConnectionTagEndpoints.PlaceAsync(files, open, cancellationToken) is not { } place)
            return TypedResults.NotFound();

        var asked = paths.OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var (rows, missing) = await RowsAsync(tags.Tags, place, asked, cancellationToken);
        if (
            missing.Count > 0
            && await IndexAsync(files, (open, place), (tags, missing), cancellationToken) is { } failed
        )
        {
            return failed;
        }

        (rows, missing) = missing.Count > 0 ? await RowsAsync(tags.Tags, place, asked, cancellationToken) : (rows, []);
        if (missing.Count > 0)
            return Problem("paths", $"“{missing[0]}” is not a file of the connection.");
        // A rule set on a file at another path (renamed outside SFE) applies too.
        if (rows.Any(row => !open.Folders.At(row.Path, row.ProviderId).Has(Rights.AssignTags)))
            return Problem("paths", NotFiles);

        var add = (change.Add ?? []).Distinct().ToList();
        var remove = (change.Remove ?? []).Distinct().Where(t => !add.Contains(t)).ToList();
        var (result, unknown) = await tags.Tags.ChangeAsync(place, rows, (add, remove), cancellationToken);
        return result is not null
            ? TypedResults.Ok(result)
            : Problem(add.Contains(unknown!.Value) ? "add" : "remove", $"Tag {unknown} does not exist.");
    }

    private static IResult? Invalid(FileTagChange change) =>
        change.Paths is not { Count: > 0 } paths ? Problem("paths", "Name at least one file.")
        : paths.Count > TagService.MaxFilesPerChange
            ? Problem("paths", $"At most {TagService.MaxFilesPerChange} files can be tagged at once.")
        : null;

    /// <summary>The rows of the files asked the index holds, and the paths it holds none of.</summary>
    private static async Task<(IReadOnlyList<RemoteTaggedFile> Rows, List<string> Missing)> RowsAsync(
        RemoteFileTags tags,
        RemoteTagPlace place,
        IReadOnlyList<string> asked,
        CancellationToken cancellationToken
    )
    {
        var rows = await tags.RowsAsync(place, asked, cancellationToken);
        var held = rows.Select(row => place.KeyOf(row.Path)).ToHashSet(StringComparer.Ordinal);
        return (rows, [.. asked.Where(path => !held.Contains(place.KeyOf(path)))]);
    }

    /// <summary>
    /// The files missing from the index asked of the provider (in a turn of its gate) and written into this
    /// library's index; the answer to give when that can't be done, null when done.
    /// </summary>
    private static async Task<IResult?> IndexAsync(
        ConnectionFileServices files,
        (OpenConnection Connection, RemoteTagPlace Place) where,
        (ConnectionTagServices Services, List<string> Paths) missing,
        CancellationToken cancellationToken
    )
    {
        var (open, place) = where;
        var id = open.Connection.Id;
        var state = await missing.Services.Store.StateAsync(id, cancellationToken);
        // An index of another root or account (dropped by the next sync) holds none of these files either.
        var current = state?.RootPath == place.Target?.RootPath && state?.AccountKey == place.Target?.AccountKey;
        if (state is null || !current || missing.Paths.Count > MaxFetched)
        {
            // Started when never made, or old: the sync brings the files.
            await missing.Services.Indexes.OpenedAsync(id, cancellationToken);
            return NotIndexed();
        }
        var answer = await files.RunAsync(open, async storage =>
        {
            foreach (var path in missing.Paths)
            {
                var item = await storage.GetAsync(path, cancellationToken);
                if (item is null || item.IsFolder || open.Check(path, item, Rights.AssignTags) != PathVerdict.Allowed)
                    return Problem("paths", NotFiles);
                var put = ConnectionItemChange.Put(id, open.Capabilities, item);
                await missing.Services.Updates.ApplyAsync(put, cancellationToken);
            }
            return TypedResults.Empty;
        });
        return answer is EmptyHttpResult ? null : answer;
    }

    private static IResult NotIndexed() =>
        TypedResults.Problem(
            "The index doesn't have these files yet: the connection's next sync adds them. "
                + "Try again once it has run.",
            statusCode: StatusCodes.Status409Conflict,
            type: ProblemTypes.ConnectionNotIndexed
        );

    private static IResult Problem(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
