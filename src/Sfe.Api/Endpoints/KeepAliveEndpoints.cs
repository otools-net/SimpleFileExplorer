using Microsoft.Extensions.Options;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.KeepAlive;
using Otools.Net.Sfe.Core.Options;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Api.Endpoints;

public static class KeepAliveEndpoints
{
    private const string PingKeepAliveDescription =
        "Waits for the pings, at most `Sfe:KeepAlive:TimeoutSeconds`; a folder still blocked from an earlier "
        + "ping is not pinged again.";

    public static RouteGroupBuilder MapKeepAliveEndpoints(this RouteGroupBuilder api)
    {
        var keepAlive = api.MapGroup("/keep-alive").WithTags("Keep alive");

        keepAlive
            .MapGet("/", (KeepAliveService service, LibraryPaths paths, IOptionsMonitor<SfeOptions> options) =>
                TypedResults.Ok(View(service.Status(), paths, options.CurrentValue))
            )
            .RequireRight(Rights.KeepAliveSettings)
            .WithName("GetKeepAlive")
            .WithSummary(
                "Whether the space's library folder is kept alive on its file server, and what the last pings found."
            );

        keepAlive
            .MapPost(
                "/ping",
                async (
                    KeepAliveService service,
                    LibraryPaths paths,
                    IOptionsMonitor<SfeOptions> options,
                    CancellationToken cancellationToken
                ) => TypedResults.Ok(View(await service.PingNowAsync(cancellationToken), paths, options.CurrentValue))
            )
            .RequireRight(Rights.KeepAliveSettings)
            .WithName("PingKeepAlive")
            .WithSummary(
                "Pings the space's folders now (also while keep alive is off, as with `Always`), "
                    + "and answers their state."
            )
            .WithDescription(PingKeepAliveDescription);

        return api;
    }

    private static KeepAliveView View(IReadOnlyList<KeepAliveFolder> folders, LibraryPaths paths, SfeOptions options) =>
        new(
            options.KeepAlive.Mode,
            options.KeepAlive.IntervalSeconds,
            options.KeepAlive.Method,
            folders
                .Select(f => new KeepAliveFolderView(
                    paths.ToRelative(f.Path) ?? string.Empty,
                    f.IsRoot,
                    f.IsNetwork,
                    f.LastPing,
                    f.Duration?.TotalMilliseconds,
                    f.Error?.Replace(paths.Root, "(library folder)", LibraryPaths.PathComparison),
                    f.FailingSince,
                    f.Waiting,
                    f.Method
                ))
                .ToList()
        );
}
