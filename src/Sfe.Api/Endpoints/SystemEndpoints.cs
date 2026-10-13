using Microsoft.AspNetCore.Http.HttpResults;
using Sfe.Api.Maintenance;
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

        system.MapGet("/backup", async (BackupSources backups, MigrationCoordinator migration, CancellationToken cancellationToken) =>
                MigrationEndpoints.BackupFile(await backups.WriteAsync(migration.DatabaseVersion, cancellationToken)))
            .RequireRight(Rights.Backup)
            .WithName("DownloadBackup")
            .WithSummary("Downloads a backup (zip) of the databases and the configuration files.")
            .WithDescription("""
                The databases of the accounts and of every library's index (SQLite: copied while the app keeps working; SQL
                Server: `BACKUP DATABASE … COPY_ONLY`, see `Sfe:Backup:SqlServerFolder`), `settings.json`, the
                `appsettings*.json` of the content root, a `manifest.json` saying where each came from and a `README.txt`.
                Thumbnail and trickplay caches are not included. It holds password hashes and connection strings.
                """);

        return api;
    }
}
