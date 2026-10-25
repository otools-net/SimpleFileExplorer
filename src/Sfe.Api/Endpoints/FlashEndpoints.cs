using Microsoft.AspNetCore.Http.HttpResults;
using Sfe.Api.Security;
using Sfe.Api.Tenancy;
using Sfe.Core.Access;
using Sfe.Core.Files;
using Sfe.Core.Paths;
using Sfe.Core.Settings;

namespace Sfe.Api.Endpoints;

/// <param name="Path">The movie (<c>.swf</c>), relative to the library root.</param>
public sealed record FlashLinkRequest(string? Path);

/// <summary>
/// Flash movies for the viewer of the web client (agents/docs/features/flash-viewer.md). Ruffle plays them in a frame
/// of no origin (<see cref="SecurityHeaders.FlashPolicy"/>): a movie can't send the session's cookie, so it loads its
/// files through a link of <see cref="FlashLinks"/>, read only and limited to its folder.
/// </summary>
public static class FlashEndpoints
{
    public static RouteGroupBuilder MapFlashEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/files/flash-links", CreateLink)
            .RequireRight(Rights.ViewFiles)
            .FolderScoped("files.view at the movie")
            .WithName("CreateFlashLink")
            .WithTags("Files")
            .WithSummary("A link that plays a Flash movie (`.swf`) in the viewer, opening the files of its folder to it, read only.")
            .WithDescription($$"""
                The link (`api/files/flash/{token}/{name}`, relative to the app) works for {{FlashLinks.Lifetime.TotalHours:0}}
                hours, and stops earlier when the account changes its password, is disabled, or loses the right to view
                the files. 404 when the library doesn't play Flash movies (`Player:Flash`) or the file isn't a `.swf`.
                """);

        api.MapGet("/files/flash/{token}/{**entry}", GetEntry)
            .AllowAnonymous()
            .WithMetadata(new RightMetadata([Rights.ViewFiles]))
            .FolderScoped("the token's subject: files.view at each file, as the rules are now")
            .WithName("GetFlashFile")
            .WithTags("Files")
            .WithSummary("A file of a Flash movie's folder (the movie, its levels, music, data), for Ruffle: the token stands for the sign-in.")
            .WithDescription("""
                `entry` is relative to the movie's folder; its subfolders are open, nothing above it. Answered to any
                origin (the player's frame has none) without credentials, sandboxed as the library's files are.
                """);
        return api;
    }

    private static async Task<Results<Ok<FlashLink>, NotFound, ForbidHttpResult>> CreateLink(
        LibraryPaths paths, CurrentAccess current, FlashLinks links, ServerFiles serverFiles, WorkFolder work, SettingsService settings,
        FlashLinkRequest request, CancellationToken cancellationToken)
    {
        var path = LibraryPaths.NormalizeRelative(request.Path);
        if (path is not { Length: > 0 } || !path.EndsWith(".swf", StringComparison.OrdinalIgnoreCase)
            || !paths.TryResolve(path, out var full) || !File.Exists(full) || FileEndpoints.Protected(full, serverFiles, work))
            return TypedResults.NotFound();

        var state = await current.GetAsync();
        if (state.Check(paths, path, Rights.ViewFiles) is var verdict and not PathVerdict.Allowed)
            return verdict == PathVerdict.NotFound ? TypedResults.NotFound() : TypedResults.Forbid();
        if (!(await settings.GetEffectiveAsync(state.TenantId, null, cancellationToken)).Player.Flash)
            return TypedResults.NotFound();
        return TypedResults.Ok(links.Create(state.TenantId, state.User, path));
    }

    private static async Task<Results<PhysicalFileHttpResult, NotFound, ProblemHttpResult>> GetEntry(
        HttpContext context, FlashLinks links, AccessService accounts, RightsEvaluator rights, TenantRegistry tenants, ServerFiles serverFiles,
        SettingsService settings, string token, string? entry)
    {
        // Whatever the answer, the player's frame (of no origin) may read it: the token is the authority, not a cookie.
        context.Response.Headers.AccessControlAllowOrigin = "*";
        if (links.Read(token) is not { } claims)
            return TypedResults.NotFound();

        // Inside the movie's folder only: no "..", nothing above it.
        var relative = LibraryPaths.NormalizeRelative(entry);
        if (relative is not { Length: > 0 })
            return TypedResults.NotFound();
        var path = claims.Path.Length == 0 ? relative : $"{claims.Path}/{relative}";

        // The account as it is now: a new password or a disabled account ends the links it gave out.
        if (claims.User is { } userId)
        {
            var user = await accounts.GetSessionUserAsync(userId, context.RequestAborted);
            if (user is null || user.Disabled || user.SecurityStamp != claims.Stamp)
                return TypedResults.Problem("This movie's link no longer works: open it again.", statusCode: StatusCodes.Status403Forbidden);
        }
        var access = await rights.GetAsync(new AccessSubject(claims.Tenant, claims.User), context.RequestAborted);
        if (!access.Has(Rights.ViewFiles))
            return TypedResults.Problem("This movie's link no longer works: open it again.", statusCode: StatusCodes.Status403Forbidden);
        if (!(await settings.GetEffectiveAsync(claims.Tenant, null, context.RequestAborted)).Player.Flash)
            return TypedResults.NotFound();

        var library = (await tenants.GetAsync(claims.Tenant, context.RequestAborted))?.Services;
        var paths = library?.GetRequiredService<LibraryPaths>();
        if (paths is null || !paths.TryResolve(path, out var full) || !File.Exists(full) || FileEndpoints.Protected(full, serverFiles, library!.GetRequiredService<WorkFolder>()))
            return TypedResults.NotFound();
        // The folder rules as they are now, at the file asked for: a hidden subfolder stays hidden from the movie.
        if (new AccessState(null, null, access).Check(paths, path, Rights.ViewFiles) != PathVerdict.Allowed)
            return TypedResults.NotFound();

        return LibraryEndpoints.ServeFile(context, full, download: false);
    }
}
