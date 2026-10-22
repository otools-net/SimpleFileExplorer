using Microsoft.AspNetCore.Http.HttpResults;
using Sfe.Api.Maintenance;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Paths;
using Sfe.Core.Thumbnails;

namespace Sfe.Api.Endpoints;

/// <param name="Configured">The executable as set in <c>Thumbnails:FfmpegPath</c> (a path, or a name looked up on PATH).</param>
/// <param name="Path">Where it was found; null when it wasn't (thumbnails of videos, trickplay and streaming are off).</param>
/// <param name="Version">The version it reports, when found.</param>
public sealed record FfmpegInfo(string Configured, string? Path, string? Version);

/// <param name="Root">Full path of the library root on the server.</param>
/// <param name="Ffmpeg">The ffmpeg executable thumbnails, trickplay and streaming run.</param>
public sealed record SystemInfo(string Root, FfmpegInfo Ffmpeg);

public static class SystemEndpoints
{
    public static RouteGroupBuilder MapSystemEndpoints(this RouteGroupBuilder api)
    {
        var system = api.MapGroup("/system").WithTags("System");

        system.MapGet("/info", async (LibraryPaths paths, Ffmpeg ffmpeg) =>
                TypedResults.Ok(new SystemInfo(paths.Root, new FfmpegInfo(ffmpeg.Configured, ffmpeg.ExecutablePath, await ffmpeg.GetVersionAsync()))))
            .RequireRight(Rights.ServerSettings)
            .WithName("GetSystemInfo")
            .WithSummary("Where the library browsed is on the server's disk, and the ffmpeg executable found.");

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
