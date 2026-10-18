using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sfe.Core.Options;

namespace Sfe.Core.Backup;

/// <param name="Entry">Path in the zip file, without extension for SQL Server (<c>.bak</c> is added).</param>
/// <param name="Description">What it holds, for the manifest (e.g. "Accounts, spaces and the index of Library").</param>
public sealed record BackupDatabase(string Entry, string Description, DatabaseProvider Provider, string ConnectionString);

/// <param name="Entry">Path in the zip file.</param>
/// <param name="Path">Full path of the file on this computer.</param>
public sealed record BackupFile(string Entry, string Path);

/// <param name="Version">
/// The version the databases belong to, which a restore needs (before an update, the earlier one); null = an install
/// from before 1.37.0, which recorded none.
/// </param>
/// <param name="MadeBy">The version of SFE making the backup.</param>
/// <param name="Notes">Said in the manifest and the readme (e.g. a library whose database could not be found).</param>
public sealed record BackupContents(string? Version, string MadeBy, IReadOnlyList<BackupDatabase> Databases, IReadOnlyList<BackupFile> Files,
    IReadOnlyList<string> Notes);

/// <param name="Path">The zip file, in the temporary folder: whoever serves it deletes it.</param>
/// <param name="FileName">Name to download it as.</param>
public sealed record BackupArchive(string Path, string FileName, long Size);

/// <summary>
/// A zip file with the databases (accounts, spaces and every library's index) and the configuration files, plus a
/// manifest saying where each came from. SQLite databases are copied with SQLite's online backup, consistent while
/// the app keeps working; SQL Server writes its own backup (<c>BACKUP DATABASE … COPY_ONLY</c>), added when this
/// computer can read it. Nothing is changed in the install.
/// </summary>
public sealed class BackupWriter(IOptionsMonitor<BackupOptions> options, ILogger<BackupWriter> logger)
{
    public const string ManifestEntry = "manifest.json";
    public const string ReadmeEntry = "README.txt";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<BackupArchive> WriteAsync(BackupContents contents, CancellationToken cancellationToken = default)
    {
        var created = DateTime.Now;
        var version = contents.Version ?? "before-1.37.0";
        var fileName = $"SFE-backup-{version}-{created:yyyyMMdd-HHmmss}.zip";
        var path = Path.Combine(Path.GetTempPath(), $"sfe-backup-{Guid.NewGuid():N}.zip");
        var databases = new List<object>();
        var notes = contents.Notes.ToList();

        try
        {
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 16, FileOptions.Asynchronous))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach (var database in contents.Databases)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    databases.Add(database.Provider == DatabaseProvider.Sqlite
                        ? AddSqlite(zip, database, notes)
                        : await AddSqlServerAsync(zip, database, created, notes, cancellationToken));
                }

                var files = new List<object>();
                foreach (var file in contents.Files)
                {
                    if (!File.Exists(file.Path))
                        continue;
                    zip.CreateEntryFromFile(file.Path, file.Entry, CompressionLevel.Optimal);
                    files.Add(new { entry = file.Entry, original = file.Path });
                }

                var manifest = new
                {
                    application = "SFE",
                    version = contents.Version,
                    madeBy = contents.MadeBy,
                    created = created.ToUniversalTime(),
                    machine = Environment.MachineName,
                    databases,
                    files,
                    notes,
                };
                await WriteEntryAsync(zip, ManifestEntry, JsonSerializer.Serialize(manifest, Json), cancellationToken);
                await WriteEntryAsync(zip, ReadmeEntry, Readme(contents.Version ?? "(before 1.37.0)", created, notes), cancellationToken);
            }

            logger.LogInformation("Backup written: {Databases} databases, {Files} configuration files", contents.Databases.Count, contents.Files.Count(f => File.Exists(f.Path)));
            return new BackupArchive(path, fileName, new FileInfo(path).Length);
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    /// <summary>The online backup API copies a consistent state (the WAL included) while scans keep writing.</summary>
    private object AddSqlite(ZipArchive zip, BackupDatabase database, List<string> notes)
    {
        var source = new SqliteConnectionStringBuilder(database.ConnectionString) { Mode = SqliteOpenMode.ReadOnly, Pooling = false };
        if (!File.Exists(source.DataSource))
        {
            notes.Add($"{database.Description}: no database at {source.DataSource} (never used yet).");
            return new { entry = (string?)null, description = database.Description, provider = "sqlite", original = source.DataSource };
        }

        var copy = Path.Combine(Path.GetTempPath(), $"sfe-backup-{Guid.NewGuid():N}.db");
        try
        {
            using (var from = new SqliteConnection(source.ToString()))
            using (var to = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copy, Pooling = false }.ToString()))
            {
                from.Open();
                to.Open();
                from.BackupDatabase(to);
            }

            zip.CreateEntryFromFile(copy, database.Entry, CompressionLevel.Fastest);
            return new { entry = database.Entry, description = database.Description, provider = "sqlite", original = source.DataSource };
        }
        finally
        {
            TryDelete(copy);
        }
    }

    /// <summary>
    /// SQL Server writes the backup on its own computer: into <see cref="BackupOptions.SqlServerFolder"/> or its default
    /// backup folder. Added to the zip (and removed) when that path is readable from here, else left there.
    /// </summary>
    private async Task<object> AddSqlServerAsync(ZipArchive zip, BackupDatabase database, DateTime created, List<string> notes, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var name = connection.Database;
        var folder = options.CurrentValue.SqlServerFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            await using var ask = new SqlCommand("SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000))", connection);
            folder = await ask.ExecuteScalarAsync(cancellationToken) as string;
        }
        if (string.IsNullOrWhiteSpace(folder))
            throw new InvalidOperationException($"SQL Server has no default backup folder for {name}: set Sfe:Backup:SqlServerFolder.");

        // The path is the database server's: its separator, whatever this computer uses.
        var separator = folder.Contains('\\') || !folder.Contains('/') ? '\\' : '/';
        var file = folder.TrimEnd('\\', '/') + separator + $"SFE-{name}-{created:yyyyMMdd-HHmmss}.bak";
        await using (var backup = new SqlCommand("BACKUP DATABASE @database TO DISK = @file WITH COPY_ONLY, INIT", connection) { CommandTimeout = 0 })
        {
            backup.Parameters.AddWithValue("@database", name);
            backup.Parameters.AddWithValue("@file", file);
            await backup.ExecuteNonQueryAsync(cancellationToken);
        }

        var entry = database.Entry + ".bak";
        if (File.Exists(file))
        {
            zip.CreateEntryFromFile(file, entry, CompressionLevel.Fastest);
            TryDelete(file);
            return new { entry, description = database.Description, provider = "sqlServer", database = name };
        }

        notes.Add($"{database.Description}: SQL Server wrote the backup of {name} to {file} on the database server, which this computer cannot read; it is not in this zip.");
        return new { entry = (string?)null, description = database.Description, provider = "sqlServer", database = name, serverFile = file };
    }

    private static async Task WriteEntryAsync(ZipArchive zip, string name, string text, CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        await writer.WriteAsync(text.AsMemory(), cancellationToken);
    }

    private static string Readme(string version, DateTime created, IReadOnlyList<string> notes)
    {
        var text = new StringBuilder()
            .AppendLine($"Backup of SFE {version}, made {created:yyyy-MM-dd HH:mm:ss}.")
            .AppendLine()
            .AppendLine("databases/  the databases: accounts, spaces, settings of spaces and users, and the index (tags...) of each library.")
            .AppendLine("config/     the configuration files and the settings saved from the web pages (settings.json).")
            .AppendLine("manifest.json says where each file came from.")
            .AppendLine()
            .AppendLine("To restore: stop SFE, put each file back where manifest.json says (SQLite: replace the .db file and delete")
            .AppendLine("the -wal and -shm files next to it; SQL Server: RESTORE DATABASE from the .bak file), then start the SFE")
            .AppendLine($"version {version}. The thumbnail and trickplay caches are not included: they are made again when needed.")
            .AppendLine("This file holds password hashes and connection strings: keep it somewhere safe.");
        if (notes.Count > 0)
        {
            text.AppendLine().AppendLine("Notes:");
            foreach (var note in notes)
                text.AppendLine("- " + note);
        }

        return text.ToString();
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not delete {File}", path);
        }
    }
}
