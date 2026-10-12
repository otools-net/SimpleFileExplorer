using Microsoft.AspNetCore.Http.HttpResults;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.SystemCommands;

namespace Sfe.Api.Endpoints;

public sealed record CommandResult(string Output);

public static class SystemEndpoints
{
    public static RouteGroupBuilder MapSystemEndpoints(this RouteGroupBuilder api)
    {
        var system = api.MapGroup("/system").WithTags("System");

        system.MapPost("/shutdown", async Task<Results<Ok<CommandResult>, NotFound>> (SystemCommandService commands, CancellationToken cancellationToken) =>
                commands.CanShutdown ? TypedResults.Ok(new CommandResult(await commands.ShutdownAsync(cancellationToken))) : TypedResults.NotFound())
            .RequireRight(Rights.Shutdown)
            .WithName("Shutdown")
            .WithSummary("Shuts the host down (when enabled in settings).");

        system.MapPost("/dismount", async Task<Results<Ok<CommandResult>, NotFound>> (SystemCommandService commands, CancellationToken cancellationToken) =>
                commands.CanDismount ? TypedResults.Ok(new CommandResult(await commands.DismountAsync(cancellationToken))) : TypedResults.NotFound())
            .RequireRight(Rights.Dismount)
            .WithName("Dismount")
            .WithSummary("Runs the configured dismount command (when enabled in settings).");

        return api;
    }
}
