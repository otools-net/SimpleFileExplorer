using Microsoft.AspNetCore.Http.HttpResults;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Library;
using Sfe.Core.Paths;

namespace Sfe.Api.Endpoints;

/// <param name="Path">Relative to the library root; empty for the root.</param>
/// <param name="Name">The folder's or file's name (the library's name for the root).</param>
/// <param name="Kind">What it was when added: <c>directory</c>, or the kind of file by its extension.</param>
/// <param name="Exists">Still there: false once it was moved, renamed or deleted.</param>
public sealed record QuickLinkView(string Path, string Name, ItemKind Kind, string? Extension, bool Exists);

/// <param name="Paths">Folders and files to add, relative to the library root, in order.</param>
/// <param name="Position">
/// Where they go: before the link at this place (0-based) of the list. Links already in the list move there. Without
/// it, new links go at the end and the others stay where they are.
/// </param>
public sealed record QuickLinkAdd(IReadOnlyList<string>? Paths, int? Position);

public static class QuickLinkEndpoints
{
    public static RouteGroupBuilder MapQuickLinkEndpoints(this RouteGroupBuilder api)
    {
        var links = api.MapGroup("/me/quick-links").WithTags("Quick links").RequireRight(Rights.QuickLinks);

        links.MapGet("/", List)
            .FolderScoped("links to what the subject doesn't see show as missing")
            .WithName("GetQuickLinks")
            .WithSummary("The signed-in user's quick links in the library, in order, with whether each one is still there.");

        links.MapPost("/", Add)
            .FolderScoped("new links only to what the subject sees")
            .WithName("AddQuickLinks")
            .WithSummary("Adds folders and files to the quick links, or moves those already there; answers the whole list.")
            .WithDescription($"""
                Every new path must be a folder or file of the library. A list holds at most {QuickLinkService.MaxLinks}
                links: an addition going past it is refused as a whole.
                """);

        links.MapDelete("/", Remove)
            .FolderScoped("removes a link of the subject's own list, wherever it leads")
            .WithName("RemoveQuickLink")
            .WithSummary("Removes the quick link to `path` (the folder or file stays); answers the whole list.");

        return api;
    }

    private static async Task<Results<Ok<IReadOnlyList<QuickLinkView>>, ForbidHttpResult>> List(
        QuickLinkService service, CurrentAccess current, LibraryPaths paths, MediaClassifier classifier, CancellationToken cancellationToken)
    {
        if (await current.GetAsync() is not { User: { } user } state)
            return TypedResults.Forbid();
        return TypedResults.Ok(View(await service.GetAsync(user.Id, state.TenantId, cancellationToken), paths, classifier, state));
    }

    private static async Task<Results<Ok<IReadOnlyList<QuickLinkView>>, ValidationProblem, NotFound, ForbidHttpResult>> Add(
        QuickLinkService service, CurrentAccess current, LibraryPaths paths, MediaClassifier classifier, QuickLinkAdd input,
        CancellationToken cancellationToken)
    {
        if (await current.GetAsync() is not { User: { } user } state)
            return TypedResults.Forbid();
        if (input.Paths is not { Count: > 0 } requested)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["paths"] = ["Name at least one folder or file."] });
        if (requested.Count > QuickLinkService.MaxLinks)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["paths"] = [TooMany] });

        // Links already there may point to something gone: they can still move.
        var known = (await service.GetAsync(user.Id, state.TenantId, cancellationToken))
            .ToDictionary(l => l.Path, LibraryPaths.PathComparer);
        var entries = new List<QuickLinkEntry>();
        foreach (var raw in requested)
        {
            if (LibraryPaths.NormalizeRelative(raw) is not { } path)
                return TypedResults.NotFound();
            if (known.TryGetValue(path, out var existing))
                entries.Add(existing);
            else if (paths.TryResolve(path, out var full) && (Directory.Exists(full) || File.Exists(full)) && state.At(paths, path) is { IsVisible: true })
                entries.Add(new QuickLinkEntry(path, Directory.Exists(full)));
            else
                return TypedResults.NotFound();
        }

        var (status, links) = await service.AddAsync(user.Id, state.TenantId, entries, input.Position, cancellationToken);
        return status == QuickLinkAddStatus.Full
            ? TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["paths"] = [TooMany] })
            : TypedResults.Ok(View(links, paths, classifier, state));
    }

    private static async Task<Results<Ok<IReadOnlyList<QuickLinkView>>, ForbidHttpResult>> Remove(
        QuickLinkService service, CurrentAccess current, LibraryPaths paths, MediaClassifier classifier, string? path,
        CancellationToken cancellationToken)
    {
        if (await current.GetAsync() is not { User: { } user } state)
            return TypedResults.Forbid();
        var links = await service.RemoveAsync(user.Id, state.TenantId, LibraryPaths.NormalizeRelative(path) ?? path ?? "", cancellationToken);
        return TypedResults.Ok(View(links, paths, classifier, state));
    }

    private static readonly string TooMany = $"Quick links hold at most {QuickLinkService.MaxLinks} folders and files: remove some first.";

    /// <summary>The links as the sidebar shows them; one leading where the subject no longer sees is shown as missing.</summary>
    private static IReadOnlyList<QuickLinkView> View(IReadOnlyList<QuickLinkEntry> links, LibraryPaths paths, MediaClassifier classifier, AccessState state) =>
        [.. links.Select(link =>
        {
            var name = link.Path.Length == 0 ? paths.RootName : link.Path[(link.Path.LastIndexOf('/') + 1)..];
            var exists = paths.TryResolve(link.Path, out var full) && (link.IsFolder ? Directory.Exists(full) : File.Exists(full))
                && state.At(paths, link.Path) is { IsVisible: true };
            if (link.IsFolder)
                return new QuickLinkView(link.Path, name, ItemKind.Directory, null, exists);
            var extension = LibraryBrowser.NormalizeExtension(Path.GetExtension(name));
            return new QuickLinkView(link.Path, name, classifier.Classify(extension), extension, exists);
        })];
}
