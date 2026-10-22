using Microsoft.Extensions.Options;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.KeepAlive;
using Sfe.Core.Options;
using Sfe.Core.Paths;

namespace Sfe.Api.Endpoints;

/// <param name="Path">Relative to the library root; empty for the root itself.</param>
/// <param name="IsRoot">The library folder itself; the others are the more folders of the settings.</param>
/// <param name="IsNetwork">Whether it is on a network share; null until the first ping looked.</param>
/// <param name="LastPing">When the last ping ended; null: none yet.</param>
/// <param name="DurationMilliseconds">How long the last ping took, when it worked.</param>
/// <param name="Error">Why the last ping failed; null when it worked. The library's place on the server is not named.</param>
/// <param name="FailingSince">The first failed ping of the current run of failures.</param>
/// <param name="Waiting">A ping is still blocked on the folder (the share does not answer).</param>
/// <param name="Method">How the last ping was made (a folder read, or a file written in the work folder); null: none yet.</param>
public sealed record KeepAliveFolderView(
    string Path, bool IsRoot, bool? IsNetwork, DateTimeOffset? LastPing, double? DurationMilliseconds, string? Error, DateTimeOffset? FailingSince, bool Waiting,
    KeepAliveMethod? Method);

/// <param name="Mode">The mode in effect for the space.</param>
/// <param name="Method">How the library folder is to be pinged (the more folders are always read).</param>
/// <param name="Folders">The folders pinged so far, since the server started: the library folder first.</param>
public sealed record KeepAliveView(KeepAliveMode Mode, int IntervalSeconds, KeepAliveMethod Method, IReadOnlyList<KeepAliveFolderView> Folders);

public static class KeepAliveEndpoints
{
    public static RouteGroupBuilder MapKeepAliveEndpoints(this RouteGroupBuilder api)
    {
        var keepAlive = api.MapGroup("/keep-alive").WithTags("Keep alive");

        keepAlive.MapGet("/", (KeepAliveService service, LibraryPaths paths, IOptionsMonitor<SfeOptions> options) =>
                TypedResults.Ok(View(service.Status(), paths, options.CurrentValue)))
            .RequireRight(Rights.KeepAliveSettings)
            .WithName("GetKeepAlive")
            .WithSummary("Whether the space's library folder is kept alive on its file server, and what the last pings found.");

        keepAlive.MapPost("/ping", async (KeepAliveService service, LibraryPaths paths, IOptionsMonitor<SfeOptions> options, CancellationToken cancellationToken) =>
                TypedResults.Ok(View(await service.PingNowAsync(cancellationToken), paths, options.CurrentValue)))
            .RequireRight(Rights.KeepAliveSettings)
            .WithName("PingKeepAlive")
            .WithSummary("Pings the space's folders now (also while keep alive is off, as with `Always`), and answers their state.")
            .WithDescription("Waits for the pings, at most `Sfe:KeepAlive:TimeoutSeconds`; a folder still blocked from an earlier ping is not pinged again.");

        return api;
    }

    private static KeepAliveView View(IReadOnlyList<KeepAliveFolder> folders, LibraryPaths paths, SfeOptions options) =>
        new(options.KeepAlive.Mode, options.KeepAlive.IntervalSeconds, options.KeepAlive.Method, folders
            .Select(f => new KeepAliveFolderView(
                paths.ToRelative(f.Path) ?? string.Empty,
                f.IsRoot,
                f.IsNetwork,
                f.LastPing,
                f.Duration?.TotalMilliseconds,
                f.Error?.Replace(paths.Root, "(library folder)", LibraryPaths.PathComparison),
                f.FailingSince,
                f.Waiting,
                f.Method))
            .ToList());
}
