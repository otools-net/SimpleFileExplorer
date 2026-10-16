using Sfe.Core.Paths;
using Microsoft.AspNetCore.Http.HttpResults;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Tagging;

namespace Sfe.Api.Endpoints;

/// <param name="Path">File, relative to the library root.</param>
public sealed record ViewedFile(string Path);

public static class TagEndpoints
{
    private const int DefaultTagLimit = 100;
    private const int MaxTagLimit = 1000;

    public static RouteGroupBuilder MapTagEndpoints(this RouteGroupBuilder api)
    {
        var tags = api.MapGroup("/tags").WithTags("Tags");

        tags.MapGet("/", Search)
            .RequireRight(Rights.ViewTags)
            .FolderScoped("file counts count only the files the subject sees")
            .FolderScoped("file counts count only the files the subject sees")
            .WithName("SearchTags")
            .WithSummary("Lists tags, or finds the ones whose name, shorthand or alias contains `q`.")
            .WithDescription("Names starting with `q` come first. `descendants` lists the tags inheriting from a match.");

        tags.MapGet("/sidebar", async (TagService service, CurrentAccess current, CancellationToken cancellationToken) =>
                await service.GetSidebarAsync(cancellationToken, (await current.GetAsync()).Visible()))
            .RequireRight(Rights.ViewTags)
            .FolderScoped("file counts (and the most used tags) count only the files the subject sees")
            .WithName("GetTagSidebar")
            .WithSummary("Most used tags, last searched tags, tags of the last viewed files, and random other tags.");

        tags.MapDelete("/history", async (TagService service, CancellationToken cancellationToken) =>
            {
                await service.ClearHistoryAsync(cancellationToken);
                return TypedResults.NoContent();
            })
            .RequireRight(Rights.ManageTags)
            .WithName("ClearTagHistory")
            .WithSummary("Forgets the searched and viewed tags (the sidebar's history lists).");

        tags.MapGet("/{id:int}", Get)
            .RequireRight(Rights.ViewTags)
            .FolderScoped("the file count counts only the files the subject sees")
            .FolderScoped("the file count counts only the files the subject sees")
            .WithName("GetTag")
            .WithSummary("A tag with its aliases and parent tags.");

        tags.MapPost("/", Create)
            .RequireRight(Rights.ManageTags)
            .WithName("CreateTag")
            .WithSummary("Creates a tag.");

        tags.MapPut("/{id:int}", Update)
            .RequireRight(Rights.ManageTags)
            .WithName("UpdateTag")
            .WithSummary("Replaces a tag's name, shorthand, aliases, parents, color and properties.");

        tags.MapDelete("/{id:int}", Delete)
            .RequireRight(Rights.ManageTags)
            .WithName("DeleteTag")
            .WithSummary("Deletes a tag and removes it from every file. Built-in tags cannot be deleted.");

        var files = api.MapGroup("/file-tags").WithTags("Tags");

        files.MapGet("/", GetFileTags)
            .RequireRight(Rights.ViewTags)
            .FolderScoped("tags.view at the file")
            .FolderScoped("tags.view at the file")
            .WithName("GetFileTags")
            .WithSummary("Tags of a file, also grouped by category tag.");

        files.MapPost("/", ChangeFileTags)
            .RequireRight(Rights.AssignTags)
            .FolderScoped("tags.assign at every file, else nothing changes")
            .FolderScoped("tags.assign at every file, else nothing changes")
            .WithName("ChangeFileTags")
            .WithSummary("Adds and removes tags on one or more files.")
            .WithDescription("Files that are not indexed yet are indexed (their folder only) before being tagged.");

        files.MapGet("/scope", GetScopeTags)
            .RequireRight(Rights.ViewTags)
            .FolderScoped("404 for a scope the subject doesn't see; only the files it sees count")
            .FolderScoped("404 for a scope the subject doesn't see; only the files it sees count")
            .WithName("GetScopeTags")
            .WithSummary("Tags of the files of a folder (or a folder tree), or of the matches of a search, with the number of files having each.")
            .WithDescription("Without `q`: the indexed files of the folder `path`, and of its sub-folders with `recursive`. With `q`: what that search finds in `path`.");

        files.MapPost("/scope", ChangeScopeTags)
            .RequireRight(Rights.AssignTags)
            .FolderScoped("404 for a scope the subject doesn't see; only the files where it may tag change")
            .FolderScoped("404 for a scope the subject doesn't see; only the files where it may tag change")
            .WithName("ChangeScopeTags")
            .WithSummary("Adds and removes tags on every file of a folder (or a folder tree), or on every match of a search.")
            .WithDescription("A folder is indexed first (with its sub-folders when `recursive`), so that its files not indexed yet are tagged too; "
                + "that is refused (409) while a scan runs. A search changes the files it finds. The files are changed by batches: "
                + "a request that is cancelled keeps the batches already written.");

        files.MapPost("/viewed", Viewed)
            .RequireRight(Rights.ViewFiles)
            .FolderScoped("files.view at the file")
            .FolderScoped("files.view at the file")
            .WithName("RecordFileViewed")
            .WithSummary("Records that a file was viewed: its tags become the last viewed tags.");

        return api;
    }

    private static async Task<Results<NoContent, NotFound>> Viewed(
        TagService service, LibraryPaths paths, CurrentAccess current, ViewedFile file, CancellationToken cancellationToken) =>
        (await current.GetAsync()).Check(paths, file.Path, Rights.ViewFiles) == PathVerdict.Allowed && await service.RecordViewedAsync(file.Path, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();

    private static async Task<Ok<TagSearchResults>> Search(
        TagService service, CurrentAccess current, CancellationToken cancellationToken, string? q = null, int? limit = null) =>
        TypedResults.Ok(await service.SearchAsync(q, Math.Clamp(limit ?? DefaultTagLimit, 1, MaxTagLimit), cancellationToken, (await current.GetAsync()).Visible()));

    private static async Task<Results<Ok<TagDetails>, NotFound>> Get(TagService service, CurrentAccess current, int id, CancellationToken cancellationToken) =>
        await service.GetAsync(id, cancellationToken, (await current.GetAsync()).Visible()) is { } tag ? TypedResults.Ok(tag) : TypedResults.NotFound();

    private static async Task<Results<Created<TagDetails>, ValidationProblem, NotFound>> Create(TagService service, TagInput input, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(input, cancellationToken);
        return result.Status switch
        {
            TagEditStatus.Ok => TypedResults.Created($"/api/tags/{result.Tag!.Id}", result.Tag),
            TagEditStatus.Invalid => TypedResults.ValidationProblem(result.Errors!),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<Results<Ok<TagDetails>, ValidationProblem, NotFound>> Update(TagService service, int id, TagInput input, CancellationToken cancellationToken)
    {
        var result = await service.UpdateAsync(id, input, cancellationToken);
        return result.Status switch
        {
            TagEditStatus.Ok => TypedResults.Ok(result.Tag!),
            TagEditStatus.Invalid => TypedResults.ValidationProblem(result.Errors!),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> Delete(TagService service, int id, CancellationToken cancellationToken) =>
        await service.DeleteAsync(id, cancellationToken) switch
        {
            TagDeleteStatus.Deleted => TypedResults.NoContent(),
            TagDeleteStatus.BuiltIn => TypedResults.Problem("Built-in tags cannot be deleted.", statusCode: StatusCodes.Status409Conflict),
            _ => TypedResults.NotFound(),
        };

    private static async Task<Results<Ok<FileTags>, NotFound>> GetFileTags(
        TagService service, LibraryPaths paths, CurrentAccess current, string path, CancellationToken cancellationToken) =>
        (await current.GetAsync()).Check(paths, path, Rights.ViewTags) == PathVerdict.Allowed && await service.GetFileTagsAsync(path, cancellationToken) is { } tags
            ? TypedResults.Ok(tags)
            : TypedResults.NotFound();

    private static async Task<Results<Ok<FileTagChangeResult>, ValidationProblem>> ChangeFileTags(
        TagService service, LibraryPaths paths, CurrentAccess current, FileTagChange change, CancellationToken cancellationToken)
    {
        // A file the subject may not tag fails the whole change, as a file that isn't there does.
        if (!(await current.GetAsync()).AllowsAll(paths, change.Paths ?? [], Rights.AssignTags))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["paths"] = ["Some of these files are not files of the library."] });
        var (result, errors) = await service.ChangeFileTagsAsync(change, cancellationToken);
        return result is null ? TypedResults.ValidationProblem(errors!) : TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<ScopeTags>, NotFound>> GetScopeTags(
        ScopeTagger tagger, LibraryPaths paths, CurrentAccess current, CancellationToken cancellationToken, string? path = null, bool recursive = false, string? q = null)
    {
        var state = await current.GetAsync();
        if (state.At(paths, path) is not { IsVisible: true })
            return TypedResults.NotFound();
        return await tagger.GetTagsAsync(new TagScope(path, recursive, q), cancellationToken, state.Visible()) is { } tags ? TypedResults.Ok(tags) : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<ScopeTagChangeResult>, ValidationProblem, NotFound, ProblemHttpResult>> ChangeScopeTags(
        ScopeTagger tagger, LibraryPaths paths, CurrentAccess current, ScopeTagChange change, CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        if (state.At(paths, change.Path) is not { IsVisible: true })
            return TypedResults.NotFound();
        var where = state.Access.FilterFor(Rights.AssignTags) is { IsEverything: false } filter ? filter : null;
        var outcome = await tagger.ChangeAsync(change, cancellationToken, where);
        return outcome.Status switch
        {
            ScopeTagStatus.Ok => TypedResults.Ok(outcome.Result!),
            ScopeTagStatus.Invalid => TypedResults.ValidationProblem(outcome.Errors!),
            ScopeTagStatus.ScanRunning => TypedResults.Problem("A scan is running: try again when it has finished.", statusCode: StatusCodes.Status409Conflict),
            _ => TypedResults.NotFound(),
        };
    }
}
