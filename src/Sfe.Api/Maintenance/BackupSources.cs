using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sfe.Api.Tenancy;
using Sfe.Core;
using Sfe.Core.Backup;
using Sfe.Core.Data;
using Sfe.Core.Options;
using Sfe.Core.Settings;
using Sfe.Core.Upgrades;

namespace Sfe.Api.Maintenance;

/// <summary>
/// What a backup holds: the database of <c>ConnectionStrings:Sfe</c> (accounts, spaces, the first library's index),
/// the index of every other library, the settings file and the configuration files of the content root. Read without
/// the libraries' services, so that it works while the install waits for its update too.
/// </summary>
public sealed class BackupSources(
    IConfiguration configuration, IWebHostEnvironment environment, IDbContextFactory<AccessDbContext> dbFactory, SettingsStore settings, BackupWriter writer,
    ILogger<BackupSources> logger)
{
    /// <param name="installedVersion">The version the databases belong to (<see cref="MigrationCoordinator.DatabaseVersion"/>).</param>
    public async Task<BackupArchive> WriteAsync(string? installedVersion, CancellationToken cancellationToken = default)
    {
        var contentRoot = environment.ContentRootPath;
        var provider = configuration.GetSection($"{SfeOptions.SectionName}:{nameof(SfeOptions.Database)}").Get<DatabaseOptions>()?.Provider ?? DatabaseProvider.SqlServer;
        var main = configuration.GetConnectionString(ServiceCollectionExtensions.ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ServiceCollectionExtensions.ConnectionStringName}' is not configured.");
        var notes = new List<string>();
        var databases = new List<BackupDatabase>
        {
            new(Entry(provider, Resolve(provider, main, contentRoot), "databases"), "Accounts, spaces, settings and the index of the first library", provider,
                Resolve(provider, main, contentRoot)),
        };

        foreach (var (slug, name, connectionString) in await OtherLibrariesAsync(notes, cancellationToken))
        {
            var resolved = Resolve(provider, connectionString ?? TenantHost.ConnectionString(slug, configuration, contentRoot), contentRoot);
            databases.Add(new BackupDatabase(Entry(provider, resolved, "databases/tenants"), $"Index of the space {name} ({slug})", provider, resolved));
        }

        var files = new List<BackupFile> { new("config/settings.json", settings.FilePath) };
        foreach (var name in new[] { "appsettings.json", $"appsettings.{environment.EnvironmentName}.json" }.Distinct())
            files.Add(new BackupFile("config/" + name, Path.Combine(contentRoot, name)));

        return await writer.WriteAsync(new BackupContents(installedVersion, Api.AppVersion.Version, databases, files, notes), cancellationToken);
    }

    /// <summary>The libraries but the first; none before 1.34.0 (no such table).</summary>
    private async Task<List<(string Slug, string Name, string? ConnectionString)>> OtherLibrariesAsync(List<string> notes, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (!await InstalledVersion.HasTableAsync(db, "Tenants", cancellationToken))
                return [];
            // Only the columns needed: the model may be ahead of an install waiting for its update.
            return (await db.Tenants.AsNoTracking().Where(t => !t.IsDefault).OrderBy(t => t.Slug)
                    .Select(t => new { t.Slug, t.Name, t.ConnectionString }).ToListAsync(cancellationToken))
                .Select(t => (t.Slug, t.Name, t.ConnectionString))
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Backup: cannot list the spaces; only the first library's database is included");
            notes.Add("The other spaces could not be listed: only the first library's database is included. " + ex.Message);
            return [];
        }
    }

    /// <summary>A relative SQLite file resolved against the content root, as the app does (without creating its folder).</summary>
    private static string Resolve(DatabaseProvider provider, string connectionString, string contentRoot)
    {
        if (provider != DatabaseProvider.Sqlite)
            return connectionString;
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource) || builder.DataSource == ":memory:" || builder.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return connectionString;
        builder.DataSource = Path.GetFullPath(builder.DataSource, contentRoot);
        return builder.ToString();
    }

    private static string Entry(DatabaseProvider provider, string connectionString, string folder) => provider == DatabaseProvider.Sqlite
        ? $"{folder}/{Path.GetFileName(new SqliteConnectionStringBuilder(connectionString).DataSource)}"
        : $"{folder}/{(new SqlConnectionStringBuilder(connectionString).InitialCatalog is { Length: > 0 } name ? name : "database")}";
}

/// <summary>
/// Backups made on the update page, kept until downloaded once (or for <see cref="Lifetime"/>): the page asks for one
/// with the setup code, then the browser downloads it by its token like any file, without holding it in memory.
/// </summary>
public sealed class BackupDownloads(ILogger<BackupDownloads> logger)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, (BackupArchive Archive, DateTime Expires)> _archives = new();

    public string Add(BackupArchive archive)
    {
        Purge();
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        _archives[token] = (archive, DateTime.UtcNow + Lifetime);
        return token;
    }

    /// <summary>The backup of <paramref name="token"/>, forgotten here: the caller serves it and deletes it.</summary>
    public BackupArchive? Take(string token)
    {
        Purge();
        return _archives.TryRemove(token, out var entry) ? entry.Archive : null;
    }

    private void Purge()
    {
        foreach (var (token, entry) in _archives)
        {
            if (entry.Expires > DateTime.UtcNow || !_archives.TryRemove(token, out _))
                continue;
            try
            {
                File.Delete(entry.Archive.Path);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Could not delete the expired backup {File}", entry.Archive.Path);
            }
        }
    }
}
