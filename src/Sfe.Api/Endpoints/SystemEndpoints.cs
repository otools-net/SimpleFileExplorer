using Sfe.Api.Maintenance;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Paths;
using Sfe.Core.Thumbnails;

namespace Sfe.Api.Endpoints;

public static class SystemEndpoints
{
    private const string DownloadBackupDescription = """
        The databases of the accounts and of every library's index (SQLite: copied while the app keeps working;
        SQL Server: `BACKUP DATABASE … COPY_ONLY`, see `Sfe:Backup:SqlServerFolder`), `settings.json`, the
        `appsettings*.json` of the content root, a `manifest.json` saying where each came from and a
        `README.txt`. Thumbnail and trickplay caches are not included. It holds password hashes and connection
        strings.
        """;

    public static RouteGroupBuilder MapSystemEndpoints(this RouteGroupBuilder api)
    {
        var system = api.MapGroup("/system").WithTags("System");

        system
            .MapGet("/info", async (LibraryPaths paths, Ffmpeg ffmpeg) =>
                TypedResults.Ok(
                    new SystemInfo(
                        paths.Root,
                        new FfmpegInfo(ffmpeg.Configured, ffmpeg.ExecutablePath, await ffmpeg.GetVersionAsync())
                    )
                )
            )
            .RequireRight(Rights.ServerSettings)
            .WithName("GetSystemInfo")
            .WithSummary("Where the library browsed is on the server's disk, and the ffmpeg executable found.");

        system
            .MapGet(
                "/backup",
                async (BackupSources backups, MigrationCoordinator migration, CancellationToken cancellationToken) =>
                    MigrationEndpoints.BackupFile(
                        await backups.WriteAsync(migration.DatabaseVersion, cancellationToken)
                    )
            )
            .RequireRight(Rights.Backup)
            .WithName("DownloadBackup")
            .WithSummary("Downloads a backup (zip) of the databases and the configuration files.")
            .WithDescription(DownloadBackupDescription);

        return api;
    }
}
