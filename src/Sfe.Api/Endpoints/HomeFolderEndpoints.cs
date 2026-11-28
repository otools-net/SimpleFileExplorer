using Microsoft.AspNetCore.Http.HttpResults;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>The signed-in account's home folder in the library (agents/docs/features/home-folder.md).</summary>
public static class HomeFolderEndpoints
{
    private const string GetDescription = """
        The folder the account chose, else the default of its groups in the library (a group's default may name the
        account: `Homes/{user}`). Never created by SFE: `exists` is false for a folder not made yet.
        """;

    public static RouteGroupBuilder MapHomeFolderEndpoints(this RouteGroupBuilder api)
    {
        var home = api.MapGroup("/me/home-folder").WithTags("Home folder").RequireRight(Rights.HomeFolder);

        home.MapGet("/", Get)
            .FolderScoped("a home folder the subject doesn't see shows as missing")
            .WithName("GetHomeFolder")
            .WithSummary("The signed-in account's home folder in the library, and whether it is there.")
            .WithDescription(GetDescription);

        home.MapPut("/", Set)
            .FolderScoped("only a folder the subject sees")
            .WithName("SetHomeFolder")
            .WithSummary("Makes a folder of the library the account's home folder, over its groups' default.");

        home.MapDelete("/", Clear)
            .FolderScoped("forgets the subject's own choice, wherever it leads")
            .WithName("ClearHomeFolder")
            .WithSummary("Forgets the folder the account chose: its groups' default (if any) is its home again.");

        return api;
    }

    private static async Task<Results<Ok<HomeFolderView>, ForbidHttpResult>> Get(
        HomeFolderService service,
        CurrentAccess current,
        LibraryPaths paths,
        CancellationToken cancellationToken
    )
    {
        if (await current.GetAsync() is not { User: { } user } state)
            return TypedResults.Forbid();
        return TypedResults.Ok(View(await service.GetAsync(user.Id, state.TenantId, cancellationToken), paths, state));
    }

    private static async Task<Results<Ok<HomeFolderView>, NotFound, ForbidHttpResult>> Set(
        HomeFolderService service,
        CurrentAccess current,
        LibraryPaths paths,
        HomeFolderInput input,
        CancellationToken cancellationToken
    )
    {
        if (await current.GetAsync() is not { User: { } user } state)
            return TypedResults.Forbid();
        if (LibraryPaths.NormalizeRelative(input.Path) is not { } path || !IsSeenFolder(path, paths, state))
            return TypedResults.NotFound();
        var choice = await service.SetAsync(user.Id, state.TenantId, path, cancellationToken);
        return TypedResults.Ok(View(choice, paths, state));
    }

    private static async Task<Results<Ok<HomeFolderView>, ForbidHttpResult>> Clear(
        HomeFolderService service,
        CurrentAccess current,
        LibraryPaths paths,
        CancellationToken cancellationToken
    )
    {
        if (await current.GetAsync() is not { User: { } user } state)
            return TypedResults.Forbid();
        return TypedResults.Ok(
            View(await service.ClearAsync(user.Id, state.TenantId, cancellationToken), paths, state)
        );
    }

    private static bool IsSeenFolder(string path, LibraryPaths paths, AccessState state) =>
        paths.TryResolve(path, out var full) && Directory.Exists(full) && state.At(paths, path) is { IsVisible: true };

    private static HomeFolderView View(HomeFolderChoice choice, LibraryPaths paths, AccessState state)
    {
        var path = choice.Path;
        var name =
            path is null ? null
            : path.Length == 0 ? paths.RootName
            : path[(path.LastIndexOf('/') + 1)..];
        return new HomeFolderView(
            path,
            name,
            path is not null && IsSeenFolder(path, paths, state),
            choice.Own is not null,
            choice.Default?.Group,
            choice.Default?.Path
        );
    }
}
