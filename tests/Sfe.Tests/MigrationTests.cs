using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Sfe.Api.Endpoints;
using Sfe.Api.Maintenance;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Upgrades;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// The update of an install to the running version (<see cref="MigrationCoordinator"/>, the update page's routes) and
/// backups (<see cref="Sfe.Core.Backup.BackupWriter"/>).
/// </summary>
[TestClass]
public sealed class MigrationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private TestLibrary _library = null!;
    private readonly List<IDisposable> _disposables = [];

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Initialize() => _library = new TestLibrary();

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var disposable in Enumerable.Reverse(_disposables))
            disposable.Dispose();
        _library.Dispose();
    }

    [TestMethod]
    public async Task ANewInstallRecordsItsVersionAndOpens()
    {
        var factory = Factory();
        var client = ApiClients.Create(factory);

        var info = await MigrationAsync(client);
        await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);

        Assert.AreEqual(MigrationStatus.None, info.Status);
        Assert.AreEqual(MigrationCoordinator.AppVersion.ToString(), info.DatabaseVersion);
        Assert.IsEmpty(info.Changes);
        Assert.AreEqual(MigrationCoordinator.AppVersion.ToString(), StoredVersion());
    }

    [TestMethod]
    public async Task AnInstallOfAnEarlierVersionStaysClosedAndUntouchedUntilTheUpdateIsConfirmed()
    {
        await InstallAsync("1.35.0");
        // A per-library setting in the server's file: a regular start moves it to the library (a change to wait for).
        await File.WriteAllTextAsync(_library.SettingsPath, """{ "scan": { "excludedFolders": ["Media"] } }""", TestContext.CancellationToken);
        var before = Fingerprint();

        var factory = Factory();
        var visitor = ApiClients.Create(factory);
        var info = await MigrationAsync(visitor);
        using var config = await visitor.GetAsync("/api/config", TestContext.CancellationToken);
        using var setup = await visitor.GetAsync("/api/setup", TestContext.CancellationToken);
        using var signIn = await visitor.PostAsJsonAsync("/api/session/login", new { userName = ApiClients.AdminName, password = ApiClients.AdminPassword },
            TestContext.CancellationToken);
        using var wrong = await visitor.PostAsJsonAsync("/api/migration", new MigrationInput("not-the-setup-code"), TestContext.CancellationToken);
        var after = Fingerprint();

        Assert.AreEqual(MigrationStatus.Required, info.Status);
        Assert.AreEqual("1.35.0", info.DatabaseVersion);
        Assert.IsTrue(info.CodeConfigured);
        CollectionAssert.Contains(info.Changes.Select(c => c.Version).ToList(), MigrationCoordinator.AppVersion.ToString());
        CollectionAssert.Contains(info.Changes.Select(c => c.Version).ToList(), "1.36.0");
        CollectionAssert.DoesNotContain(info.Changes.Select(c => c.Version).ToList(), "1.35.0", "Only the versions after the installed one");
        Assert.AreEqual("Added", info.Changes.Single(c => c.Version == "1.36.0").Sections[0].Title);
        foreach (var refused in new[] { config, setup, signIn })
        {
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
            StringAssert.Contains(await refused.Content.ReadAsStringAsync(TestContext.CancellationToken), "migration-required");
        }
        Assert.AreEqual(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.AreEqual(before, after, "Neither the database nor the settings file changed");
        Assert.AreEqual("1.35.0", StoredVersion());
    }

    [TestMethod]
    public async Task WithoutAConfiguredCodeTheUpdateIsConfirmedWithTheOneTheServerMade()
    {
        await InstallAsync("1.40.0");
        var factory = ApiClients.CreateFactory(_library, settings => settings["Sfe:Setup:Code"] = "");
        _disposables.Add(factory);
        var visitor = ApiClients.Create(factory);

        var info = await MigrationAsync(visitor);
        var code = (await File.ReadAllTextAsync(Path.Combine(_library.BasePath, "setup-code.txt"), TestContext.CancellationToken)).Trim();
        using var started = await visitor.PostAsJsonAsync("/api/migration", new MigrationInput(code), TestContext.CancellationToken);

        Assert.AreEqual(MigrationStatus.Required, info.Status);
        Assert.IsTrue(info.CodeConfigured);
        Assert.IsTrue(info.CodeGenerated);
        Assert.AreEqual(HttpStatusCode.OK, started.StatusCode, await started.Content.ReadAsStringAsync(TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task TheConfirmedUpdateBringsTheInstallUpAndOpensIt()
    {
        await InstallAsync(null);
        await File.WriteAllTextAsync(_library.SettingsPath, """{ "scan": { "excludedFolders": ["Media"] } }""", TestContext.CancellationToken);

        var factory = Factory();
        var waiting = await MigrationAsync(ApiClients.Create(factory));
        await ApiClients.MigrateAsync(factory, TestContext.CancellationToken);
        var done = await MigrationAsync(ApiClients.Create(factory));
        var admin = await ApiClients.SignInAsync(factory, ApiClients.AdminName, ApiClients.AdminPassword, TestContext.CancellationToken);
        using var config = await admin.GetAsync("/api/config", TestContext.CancellationToken);
        using var again = await admin.PostAsJsonAsync("/api/migration", new MigrationInput(TestLibrary.SetupCode), TestContext.CancellationToken);
        // Nothing else was saved: the emptied file is removed.
        var file = File.Exists(_library.SettingsPath) ? JsonNode.Parse(await File.ReadAllTextAsync(_library.SettingsPath, TestContext.CancellationToken)) : null;

        Assert.AreEqual(MigrationStatus.Required, waiting.Status);
        Assert.IsNull(waiting.DatabaseVersion, "Installs before 1.37.0 have no version recorded");
        Assert.IsGreaterThan(5, waiting.Changes.Count, "Every version of the changelog is listed when the installed one is unknown");
        Assert.AreEqual(MigrationStatus.None, done.Status);
        CollectionAssert.Contains(done.Log.ToList(), $"The install now belongs to SFE {MigrationCoordinator.AppVersion}.");
        Assert.AreEqual(MigrationCoordinator.AppVersion.ToString(), StoredVersion());
        Assert.AreEqual(HttpStatusCode.OK, config.StatusCode, "The app opens, with the sessions of before");
        Assert.AreEqual(HttpStatusCode.BadRequest, again.StatusCode, "Nothing more to update");
        Assert.IsNull(file?["scan"]?["excludedFolders"], "The settings moved as at a regular start");
    }

    [TestMethod]
    public async Task TheUpdateMovesTheCachesToTheNewLayout()
    {
        await InstallAsync("1.39.0");
        var photo = _library.FullPath("Media/photo.jpg");
        var old = Path.Combine(_library.ThumbnailPath, Sfe.Core.Thumbnails.ThumbnailService.Hash(Path.GetDirectoryName(photo)!),
            Sfe.Core.Thumbnails.ThumbnailService.Hash(Path.GetFileName(photo)) + ".bin");
        Directory.CreateDirectory(Path.GetDirectoryName(old)!);
        await File.WriteAllBytesAsync(old, [0xFF, 0xD8, 0xFF, 0x01], TestContext.CancellationToken);
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(1));

        var factory = Factory();
        await ApiClients.MigrateAsync(factory, TestContext.CancellationToken);
        var done = await MigrationAsync(ApiClients.Create(factory));
        var admin = await ApiClients.SignInAsync(factory, ApiClients.AdminName, ApiClients.AdminPassword, TestContext.CancellationToken);
        using var thumbnail = await admin.GetAsync("/api/thumbnails/Media/photo.jpg", TestContext.CancellationToken);

        Assert.AreEqual(MigrationStatus.None, done.Status);
        Assert.IsTrue(done.Log.Any(line => line.StartsWith("1.40.0: ", StringComparison.Ordinal)), string.Join("\n", done.Log));
        Assert.IsFalse(File.Exists(old));
        Assert.AreEqual(HttpStatusCode.OK, thumbnail.StatusCode, "The moved thumbnail is found at its new place (no ffmpeg run)");
        CollectionAssert.AreEqual(new byte[] { 0xFF, 0xD8, 0xFF, 0x01 }, await thumbnail.Content.ReadAsByteArrayAsync(TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task AnInstallOfALaterVersionIsNeverUpdated()
    {
        await InstallAsync("99.0.0");

        var factory = Factory();
        var client = ApiClients.Create(factory);
        var info = await MigrationAsync(client);
        using var start = await client.PostAsJsonAsync("/api/migration", new MigrationInput(TestLibrary.SetupCode), TestContext.CancellationToken);
        using var config = await client.GetAsync("/api/config", TestContext.CancellationToken);

        Assert.AreEqual(MigrationStatus.Newer, info.Status);
        Assert.IsEmpty(info.Changes);
        Assert.AreEqual(HttpStatusCode.BadRequest, start.StatusCode);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, config.StatusCode);
        Assert.AreEqual("99.0.0", StoredVersion());
    }

    [TestMethod]
    public async Task TheUpdatePageMakesABackupWithTheSetupCodeDownloadedOnce()
    {
        await InstallAsync("1.36.0");
        await File.WriteAllTextAsync(_library.SettingsPath, """{ "scan": { "excludedFolders": ["Media"] } }""", TestContext.CancellationToken);
        var before = Fingerprint();
        var client = ApiClients.Create(Factory());

        using var refused = await client.PostAsJsonAsync("/api/migration/backup", new MigrationInput("wrong-code-0000"), TestContext.CancellationToken);
        using var made = await client.PostAsJsonAsync("/api/migration/backup", new MigrationInput(TestLibrary.SetupCode), TestContext.CancellationToken);
        var link = (await made.Content.ReadFromJsonAsync<BackupLink>(Json, TestContext.CancellationToken))!;
        using var download = await client.GetAsync("/" + link.Url, TestContext.CancellationToken);
        var zip = await download.Content.ReadAsByteArrayAsync(TestContext.CancellationToken);
        using var twice = await client.GetAsync("/" + link.Url, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, download.StatusCode);
        Assert.AreEqual("application/zip", download.Content.Headers.ContentType?.MediaType);
        StringAssert.StartsWith(link.FileName, "SFE-backup-");
        Assert.AreEqual(link.Size, zip.Length);
        Assert.AreEqual(HttpStatusCode.NotFound, twice.StatusCode);
        AssertBackup(zip, "1.36.0");
        Assert.AreEqual(before, Fingerprint(), "Backing up changes nothing");
    }

    [TestMethod]
    public async Task AdministratorsDownloadABackupOfEveryLibraryAndTheConfiguration()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        using (var created = await admin.PostAsJsonAsync("/api/tenants", new TenantInput("Other", Slug: "other", Root: _library.FullPath("Media")), Json,
                   TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync(TestContext.CancellationToken));
        await File.WriteAllTextAsync(_library.SettingsPath, """{ "player": { "seekSeconds": 25 } }""", TestContext.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_library.BasePath, "installation.json"), """{ "Urls": "http://*:5181" }""", TestContext.CancellationToken);

        using var backup = await admin.GetAsync("/api/system/backup", TestContext.CancellationToken);
        var zip = await backup.Content.ReadAsByteArrayAsync(TestContext.CancellationToken);
        using var visitor = await ApiClients.Create(factory).GetAsync("/api/system/backup", TestContext.CancellationToken);
        using var migrationBackup = await admin.PostAsJsonAsync("/api/migration/backup", new MigrationInput(TestLibrary.SetupCode), TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, backup.StatusCode);
        StringAssert.StartsWith(backup.Content.Headers.ContentDisposition?.FileName?.Trim('"'), "SFE-backup-");
        var entries = AssertBackup(zip, MigrationCoordinator.AppVersion.ToString());
        CollectionAssert.Contains(entries, "databases/tenants/other.db", "Every library's index");
        CollectionAssert.Contains(entries, "config/installation.json", "What the installation page saved");
        Assert.AreEqual(HttpStatusCode.Unauthorized, visitor.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, migrationBackup.StatusCode, "The setup code backs up only while an update waits");
    }

    [TestMethod]
    public void VersionsCompareAsSemanticVersions()
    {
        Assert.IsTrue(Parse("1.10.0") > Parse("1.9.3"));
        Assert.IsTrue(Parse("1.3.0-beta.1") < Parse("1.3.0"));
        Assert.IsTrue(Parse("2.0.0") > Parse("1.99.99"));
        Assert.AreEqual(0, Parse("1.2.3+abcdef").CompareTo(Parse("1.2.3")));
        Assert.IsFalse(SemanticVersion.TryParse("1.2", out _));
        Assert.IsFalse(SemanticVersion.TryParse("1.2.x", out _));
        Assert.IsFalse(SemanticVersion.TryParse(null, out _));
    }

    [TestMethod]
    public void TheChangelogIsReadByVersionAndSection()
    {
        var entries = Changelog.Parse("""
            # Changelog

            Intro text.

            ## Unreleased

            ### Added

            - Not in a version yet (the edge branch).

            ## 1.2.0

            ### Added

            - One `thing`,
              continued on the next line.
            - Another.

            ### Fixed

            - A bug.

            ## 1.1.0

            ### Changed

            - Something.

            ## 1.0.0

            ### Added

            - The start.
            """);
        var between = Changelog.Between(entries, Parse("1.0.0"), Parse("1.1.5"));

        CollectionAssert.AreEqual(new[] { "1.2.0", "1.1.0", "1.0.0" }, entries.Select(e => e.Version).ToList());
        Assert.AreEqual("One `thing`, continued on the next line.", entries[0].Sections[0].Items[0]);
        CollectionAssert.AreEqual(new[] { "Added", "Fixed" }, entries[0].Sections.Select(s => s.Title).ToList());
        CollectionAssert.AreEqual(new[] { "1.1.0" }, between.Select(e => e.Version).ToList());
        Assert.HasCount(3, Changelog.Between(entries, null, Parse("1.2.0")));
    }

    [TestMethod]
    public void TheShippedChangelogHasTheRunningVersion()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "CHANGELOG.md");
        var entries = Changelog.Parse(File.ReadAllText(path));

        Assert.AreEqual(MigrationCoordinator.AppVersion.ToString(), entries[0].Version, "Each version bump adds its section to CHANGELOG.md");
        Assert.IsTrue(entries.All(e => SemanticVersion.TryParse(e.Version, out _) && e.Sections.Count > 0));
    }

    /// <summary>
    /// An install as an earlier version left it: set up and stopped, its recorded version replaced (null: none, as
    /// before 1.37.0, whose databases have no ServerProperties table).
    /// </summary>
    private async Task InstallAsync(string? version)
    {
        await using (var factory = ApiClients.CreateFactory(_library))
            await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);

        await using var connection = new SqliteConnection(_library.ConnectionString);
        await connection.OpenAsync(TestContext.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = version is null
            ? $"DROP TABLE \"{AccessDbContext.ServerPropertiesTable}\""
            : $"UPDATE \"{AccessDbContext.ServerPropertiesTable}\" SET Value = $version WHERE Key = '{ServerProperty.VersionKey}'";
        command.Parameters.AddWithValue("$version", version ?? "");
        await command.ExecuteNonQueryAsync(TestContext.CancellationToken);
        // Folds the write-ahead log into the file, so that the file alone says what the database holds.
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
        await command.ExecuteNonQueryAsync(TestContext.CancellationToken);
    }

    private string? StoredVersion()
    {
        using var connection = new SqliteConnection(_library.ConnectionString + ";Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Value FROM \"{AccessDbContext.ServerPropertiesTable}\" WHERE Key = '{ServerProperty.VersionKey}'";
        return command.ExecuteScalar() as string;
    }

    /// <summary>What the database (its tables, their rows) and the settings file hold.</summary>
    private string Fingerprint()
    {
        using var connection = new SqliteConnection(_library.ConnectionString + ";Mode=ReadOnly;Pooling=False");
        connection.Open();
        var text = new System.Text.StringBuilder();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT type, name, sql FROM sqlite_master ORDER BY type, name";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                text.AppendLine($"{reader.GetString(0)} {reader.GetString(1)} {(reader.IsDBNull(2) ? "" : reader.GetString(2))}");
        }

        foreach (var table in new[] { "Tenants", "Users", "Groups", "GroupRights", "TenantSettings", AccessDbContext.ServerPropertiesTable })
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM \"{table}\"";
            try
            {
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    text.AppendLine(table + ": " + string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "null" : reader.GetValue(i).ToString())));
            }
            catch (SqliteException)
            {
                text.AppendLine(table + ": none");
            }
        }

        if (File.Exists(_library.SettingsPath))
            text.AppendLine(File.ReadAllText(_library.SettingsPath));
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>The zip's manifest, its database (a SQLite file with the accounts) and settings file; answers its entries.</summary>
    private List<string> AssertBackup(byte[] zip, string version)
    {
        using var archive = new ZipArchive(new MemoryStream(zip));
        var entries = archive.Entries.Select(e => e.FullName).ToList();
        CollectionAssert.Contains(entries, "manifest.json");
        CollectionAssert.Contains(entries, "README.txt");
        CollectionAssert.Contains(entries, "config/settings.json");
        CollectionAssert.Contains(entries, "databases/index.db");

        using (var manifest = archive.GetEntry("manifest.json")!.Open())
            Assert.AreEqual(version, JsonNode.Parse(manifest)!["version"]!.GetValue<string>());

        var copy = Path.Combine(_library.BasePath, $"restored-{Guid.NewGuid():N}.db");
        archive.GetEntry("databases/index.db")!.ExtractToFile(copy);
        using (var connection = new SqliteConnection($"Data Source={copy};Pooling=False;Mode=ReadOnly"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Users";
            Assert.AreEqual(1L, command.ExecuteScalar(), "The administrator is in the copy");
        }

        return entries;
    }

    private async Task<MigrationInfo> MigrationAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/migration", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MigrationInfo>(Json, TestContext.CancellationToken))!;
    }

    private WebApplicationFactory<Program> Factory()
    {
        var factory = ApiClients.CreateFactory(_library);
        _disposables.Add(factory);
        return factory;
    }

    private static SemanticVersion Parse(string text) => SemanticVersion.TryParse(text, out var version) ? version : throw new FormatException(text);
}
