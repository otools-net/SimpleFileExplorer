using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Sfe.Api.Maintenance;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Logs;
using Sfe.Core.Options;
using Sfe.Core.Upgrades;

namespace Sfe.Api.Endpoints;

/// <param name="DatabaseVersion">The version the install belongs to; null when unknown (installs before 1.37.0).</param>
/// <param name="Changes">The changelog of the versions after it up to this one, newest first (while an update is due).</param>
/// <param name="Log">What the update did so far.</param>
/// <param name="CodeConfigured">The server has a setup code long enough (configured, or made by itself): the update can be confirmed.</param>
/// <param name="CodeGenerated">The code is the one the server made itself: shown in its console and kept in <paramref name="CodeFile"/>.</param>
public sealed record MigrationInfo(MigrationStatus Status, string AppVersion, string? DatabaseVersion, IReadOnlyList<ChangelogEntry> Changes,
    IReadOnlyList<string> Log, string? Error, bool CodeConfigured, bool CodeTooShort, int MinimumCodeLength, HostingKind Hosting,
    bool CodeGenerated = false, string? CodeFile = null);

public sealed record MigrationInput(string? Code);

/// <param name="Url">Where the browser downloads it, once, within <see cref="BackupDownloads.Lifetime"/>.</param>
public sealed record BackupLink(string Url, string FileName, long Size);

public static class MigrationEndpoints
{
    private static readonly Lazy<IReadOnlyList<ChangelogEntry>> Changelog = new(LoadChangelog);

    public static RouteGroupBuilder MapMigrationEndpoints(this RouteGroupBuilder api)
    {
        var migration = api.MapGroup("/migration").WithTags("Session");

        migration.MapGet("/", GetMigration)
            .AllowAnonymous()
            .WithName("GetMigration")
            .WithSummary("Whether the install waits for its update to this version, with the changes since its version.")
            .WithDescription("""
                While an update is due (`required`, `running`, `failed`), or the install belongs to a later version (`newer`),
                every other route answers 503 `migration-required`.
                """);

        migration.MapPost("/", StartMigration)
            .AllowAnonymous()
            .RequireRateLimiting(SessionEndpoints.SignInRateLimit)
            .WithName("StartMigration")
            .WithSummary("Starts updating the databases and settings to this version; needs the setup code (Sfe:Setup:Code).")
            .WithDescription("Runs in the background: poll `GET api/migration` until its status is `none` (done) or `failed`.");

        migration.MapPost("/backup", CreateBackup)
            .AllowAnonymous()
            .RequireRateLimiting(SessionEndpoints.SignInRateLimit)
            .WithName("CreateMigrationBackup")
            .WithSummary("Makes a backup (zip) of the databases and configuration files before the update; needs the setup code.")
            .WithDescription("Answers the link the browser downloads it from, once. Only while an update is due: afterwards, Administration › System.");

        migration.MapGet("/backup/{token}", DownloadBackup)
            .AllowAnonymous()
            .WithName("DownloadMigrationBackup")
            .WithSummary("Downloads a backup made by CreateMigrationBackup (once).");

        return api;
    }

    private static Ok<MigrationInfo> GetMigration(MigrationCoordinator migration, IOptionsMonitor<SetupOptions> setup, GeneratedSetupCode generated,
        ILoggerFactory loggers)
    {
        // An update to confirm needs a code: without one in the configuration, the one the server makes itself.
        if (migration.Status is MigrationStatus.Required or MigrationStatus.Failed && string.IsNullOrWhiteSpace(setup.CurrentValue.Code))
        {
            try
            {
                generated.Ensure();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                loggers.CreateLogger(typeof(MigrationEndpoints)).LogError(ex, "Cannot write the setup code to {Path}: set Sfe:Setup:Code instead", generated.FilePath);
            }
        }

        return TypedResults.Ok(Describe(migration, setup.CurrentValue));
    }

    private static async Task<Results<Ok<MigrationInfo>, ValidationProblem, ProblemHttpResult>> StartMigration(
        MigrationCoordinator migration, IOptionsMonitor<SetupOptions> setup, ILoggerFactory loggers, MigrationInput input)
    {
        if (CheckCode(setup.CurrentValue, input.Code, loggers) is { } refused)
            return refused;
        if (!await migration.StartAsync())
            return TypedResults.Problem("No update is due, or it is running already.", statusCode: StatusCodes.Status400BadRequest);
        return TypedResults.Ok(Describe(migration, setup.CurrentValue));
    }

    private static async Task<Results<Ok<BackupLink>, ValidationProblem, ProblemHttpResult>> CreateBackup(
        MigrationCoordinator migration, IOptionsMonitor<SetupOptions> setup, ILoggerFactory loggers, BackupSources backups, BackupDownloads downloads,
        MigrationInput input, CancellationToken cancellationToken)
    {
        if (CheckCode(setup.CurrentValue, input.Code, loggers) is { } refused)
            return refused;
        if (migration.Status is not (MigrationStatus.Required or MigrationStatus.Failed or MigrationStatus.Newer))
            return TypedResults.Problem("No update is waiting: back up from Administration › System.", statusCode: StatusCodes.Status400BadRequest);

        var archive = await backups.WriteAsync(migration.DatabaseVersion, cancellationToken);
        var token = downloads.Add(archive);
        return TypedResults.Ok(new BackupLink($"api/migration/backup/{token}", archive.FileName, archive.Size));
    }

    private static Results<FileStreamHttpResult, NotFound> DownloadBackup(string token, BackupDownloads downloads) =>
        downloads.Take(token) is { } archive ? BackupFile(archive) : TypedResults.NotFound();

    /// <summary>Streams a backup from its temporary file, which is deleted once sent.</summary>
    internal static FileStreamHttpResult BackupFile(Core.Backup.BackupArchive archive) =>
        TypedResults.File(
            new FileStream(archive.Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1 << 16, FileOptions.Asynchronous | FileOptions.DeleteOnClose),
            "application/zip", archive.FileName);

    /// <summary>The same code as the setup's: whoever can set it runs the server.</summary>
    private static ValidationProblem? CheckCode(SetupOptions setup, string? code, ILoggerFactory loggers)
    {
        if (!setup.HasUsableCode)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["code"] = ["The server has no setup code: set one in its environment first."] });
        if (setup.Matches(code))
            return null;
        loggers.CreateLogger(typeof(MigrationEndpoints)).LogSecurityWarning("Update refused: wrong setup code");
        return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["code"] = ["This is not the server's setup code."] });
    }

    private static MigrationInfo Describe(MigrationCoordinator migration, SetupOptions setup)
    {
        var code = setup.Code?.Trim();
        var due = migration.Status is MigrationStatus.Required or MigrationStatus.Running or MigrationStatus.Failed;
        var changes = due
            ? Core.Upgrades.Changelog.Between(Changelog.Value, SemanticVersion.TryParse(migration.DatabaseVersion, out var from) ? from : null, MigrationCoordinator.AppVersion)
            : [];
        // Where the code is kept tells about the server: only while the update page needs it.
        return new MigrationInfo(migration.Status, MigrationCoordinator.AppVersion.ToString(), migration.DatabaseVersion, changes, migration.Log, migration.Error,
            setup.HasUsableCode, !string.IsNullOrEmpty(code) && code.Length < SetupOptions.MinimumCodeLength, SetupOptions.MinimumCodeLength,
            SetupEndpoints.DetectHosting(), setup.CodeIsGenerated,
            setup.CodeIsGenerated && (due || migration.Status == MigrationStatus.Newer) ? setup.CodeFile : null);
    }

    /// <summary>CHANGELOG.md, built into the application (Sfe.Api.csproj).</summary>
    private static IReadOnlyList<ChangelogEntry> LoadChangelog()
    {
        using var stream = typeof(MigrationEndpoints).Assembly.GetManifestResourceStream("CHANGELOG.md");
        if (stream is null)
            return [];
        using var reader = new StreamReader(stream);
        return Core.Upgrades.Changelog.Parse(reader.ReadToEnd());
    }
}
