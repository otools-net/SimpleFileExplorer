using Microsoft.Extensions.Options;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.KeepAlive;
using Sfe.Core.Options;
using Sfe.Core.Paths;

namespace Sfe.Api.Endpoints;

/// <param name="Path">Relative to the library root; empty for the root itself.</param>
/// <param name="IsNetwork">Whether it is on a network share; null until the first ping looked.</param>
/// <param name="LastPing">When the last ping ended; null: none yet.</param>
/// <param name="DurationMilliseconds">How long the last ping took, when it worked.</param>
/// <param name="Error">Why the last ping failed; null when it worked. The library's place on the server is not named.</param>
/// <param name="FailingSince">The first failed ping of the current run of failures.</param>
/// <param name="Waiting">A ping is still blocked on the folder (the share does not answer).</param>
public sealed record KeepAliveFolderView(
    string Path, bool? IsNetwork, DateTimeOffset? LastPing, double? DurationMilliseconds, string? Error, DateTimeOffset? FailingSince, bool Waiting);

/// <param name="Mode">The mode in effect for the space.</param>
/// <param name="Folders">The folders pinged so far, since the server started.</param>
public sealed record KeepAliveView(KeepAliveMode Mode, int IntervalSeconds, IReadOnlyList<KeepAliveFolderView> Folders);

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
        new(options.KeepAlive.Mode, options.KeepAlive.IntervalSeconds, folders
            .Select(f => new KeepAliveFolderView(
                paths.ToRelative(f.Path) ?? string.Empty,
                f.IsNetwork,
                f.LastPing,
                f.Duration?.TotalMilliseconds,
                f.Error?.Replace(paths.Root, "(library folder)", LibraryPaths.PathComparison),
                f.FailingSince,
                f.Waiting))
            .ToList());
}
