using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sfe.Api.Endpoints;
using Sfe.Api.Tenancy;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Logs;
using Sfe.Core.Settings;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>The server's log (Administration › Server › Logs): what is kept, searched, shown to whom, written to files and forgotten.</summary>
[TestClass]
public sealed class LogTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private const string Category = "Sfe.Tests.Probe";

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
    public async Task KeepsWhatTheServerLogsAndSearchesItByTextLevelAndDate()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger(Category);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        logger.LogInformation("probe alpha {Value}", 1);
        logger.LogWarning("probe beta 100%_done[1]");
        logger.LogError(new InvalidOperationException("the inner trouble"), "probe gamma");
        await FlushAsync(factory);

        var probes = await SearchAsync(admin, "q=probe");
        var problems = await SearchAsync(admin, "q=probe&levels=warning,error");
        var literal = await SearchAsync(admin, "q=" + Uri.EscapeDataString("100%_done[1]"));
        var wildcard = await SearchAsync(admin, "q=" + Uri.EscapeDataString("probe_beta"));
        var anyCase = await SearchAsync(admin, "q=PROBE+GAMMA");
        var inException = await SearchAsync(admin, "q=inner+trouble");
        var byCategory = await SearchAsync(admin, "q=" + Category);
        var inRange = await SearchAsync(admin, $"q=probe&from={Uri.EscapeDataString(before.ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(1).ToString("O"))}");
        var later = await SearchAsync(admin, $"q=probe&from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(1).ToString("O"))}");
        var start = await SearchAsync(admin, "q=SFE&levels=information");
        using var badLevel = await admin.GetAsync("/api/logs?levels=loud", TestContext.CancellationToken);

        CollectionAssert.AreEqual(new[] { "probe gamma", "probe beta 100%_done[1]", "probe alpha 1" }, probes.Entries.Select(e => e.Message).ToArray(), "Newest first");
        CollectionAssert.AreEqual(new[] { LogEntryLevel.Error, LogEntryLevel.Warning }, problems.Entries.Select(e => e.Level).ToArray());
        Assert.AreEqual("probe beta 100%_done[1]", literal.Entries.Single().Message, "%, _ and [ are plain characters");
        Assert.IsEmpty(wildcard.Entries, "_ is no wildcard");
        Assert.AreEqual("probe gamma", anyCase.Entries.Single().Message);
        Assert.Contains("the inner trouble", anyCase.Entries.Single().Exception!);
        Assert.AreEqual("probe gamma", inException.Entries.Single().Message);
        Assert.HasCount(3, byCategory.Entries);
        Assert.HasCount(3, inRange.Entries);
        Assert.IsEmpty(later.Entries);
        Assert.IsTrue(start.Entries.Any(e => e.Message.StartsWith("SFE ", StringComparison.Ordinal) && e.TenantId is null), "The start-up's own entries are kept");
        Assert.IsTrue(probes.AllSpaces);
        Assert.AreEqual(30, probes.RetentionDays);
        Assert.IsFalse(probes.WriteFiles);
        Assert.AreEqual(_library.LogsPath, probes.FilesDirectory);
        Assert.AreEqual(HttpStatusCode.BadRequest, badLevel.StatusCode);
    }

    [TestMethod]
    public async Task WhatTheApiRefusesOrFailsWithIsLoggedWithWhoAskedButNoPassword()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);

        using var refused = await admin.PostAsJsonAsync("/api/files/jobs/compress",
            new { paths = new[] { "Media" }, target = "", level = 12, password = "secret-zip-password" }, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode);
        await FlushAsync(factory);

        var entry = (await SearchAsync(admin, "q=" + Uri.EscapeDataString("answered 400"))).Entries.Single();
        Assert.AreEqual(LogEntryLevel.Warning, entry.Level);
        Assert.AreEqual(ProblemLog.Category, entry.Category);
        StringAssert.Contains(entry.Message, "POST /api/files/jobs/compress answered 400 to admin");
        StringAssert.Contains(entry.Message, "level: A level is 0 (stored) to 9 (smallest).");
        Assert.IsEmpty((await SearchAsync(admin, "q=secret-zip-password")).Entries);
    }

    [TestMethod]
    public async Task PagesGoByKeyNewestFirst()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger(Category);
        for (var i = 1; i <= 5; i++)
            logger.LogWarning("page probe {Number}", i);
        await FlushAsync(factory);

        var first = await SearchAsync(admin, "q=page+probe&limit=2");
        var second = await SearchAsync(admin, $"q=page+probe&limit=2&before={first.Next}");
        var third = await SearchAsync(admin, $"q=page+probe&limit=2&before={second.Next}");

        CollectionAssert.AreEqual(new[] { "page probe 5", "page probe 4" }, first.Entries.Select(e => e.Message).ToArray());
        CollectionAssert.AreEqual(new[] { "page probe 3", "page probe 2" }, second.Entries.Select(e => e.Message).ToArray());
        CollectionAssert.AreEqual(new[] { "page probe 1" }, third.Entries.Select(e => e.Message).ToArray());
        Assert.IsNull(third.Next);
    }

    /// <summary>
    /// A library's entries carry it; its administrators (logs.view in the library) see only those, server
    /// administrators everything, with a filter by library; members without the right see nothing.
    /// </summary>
    [TestMethod]
    public async Task EachSpaceSeesItsOwnEntriesAndTheRightGuardsThem()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var groups = await GetAsync<List<GroupInfo>>(admin, "/api/access/groups");
        var tenantId = groups[0].TenantId;
        await CreateUserAsync(admin, new UserInput("alice", Password: "alice-password", GroupIds: [groups.Single(g => g.BuiltIn == BuiltInGroup.Administrators).Id]));
        await CreateUserAsync(admin, new UserInput("bob", Password: "bob-password", GroupIds: [groups.Single(g => g.BuiltIn == BuiltInGroup.Users).Id]));
        var alice = await ApiClients.SignInAsync(factory, "alice", "alice-password", TestContext.CancellationToken);
        var bob = await ApiClients.SignInAsync(factory, "bob", "bob-password", TestContext.CancellationToken);
        _disposables.Add(alice);
        _disposables.Add(bob);

        var host = await factory.Services.GetRequiredService<TenantRegistry>().GetAsync(tenantId, TestContext.CancellationToken);
        host!.Services.GetRequiredService<ILoggerFactory>().CreateLogger(Category).LogWarning("space probe");
        factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger(Category).LogWarning("server probe");
        await FlushAsync(factory);

        var everything = await SearchAsync(admin, "q=probe");
        var serverOnly = await SearchAsync(admin, "q=probe&tenant=0");
        var spaceOnly = await SearchAsync(admin, $"q=probe&tenant={tenantId}");
        var bySpaceAdmin = await SearchAsync(alice, "q=probe&tenant=0");
        using var byMember = await bob.GetAsync("/api/logs", TestContext.CancellationToken);
        using var byVisitor = await ApiClients.Create(factory).GetAsync("/api/logs", TestContext.CancellationToken);

        CollectionAssert.AreEquivalent(new[] { "space probe", "server probe" }, everything.Entries.Select(e => e.Message).ToArray());
        Assert.AreEqual(tenantId, everything.Entries.Single(e => e.Message == "space probe").TenantId);
        Assert.AreEqual("server probe", serverOnly.Entries.Single().Message);
        Assert.AreEqual("space probe", spaceOnly.Entries.Single().Message);
        Assert.AreEqual("space probe", bySpaceAdmin.Entries.Single().Message, "Only the space's entries, whatever tenant says");
        Assert.IsFalse(bySpaceAdmin.AllSpaces);
        Assert.IsNull(bySpaceAdmin.FilesDirectory, "Where the server keeps its files is not told");
        Assert.AreEqual(HttpStatusCode.Forbidden, byMember.StatusCode);
        Assert.IsTrue(byVisitor.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, byVisitor.StatusCode.ToString());
    }

    [TestMethod]
    public async Task TheSettingsAreTheServersAndChecked()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);

        using var saved = await PatchAsync(admin, "server", new() { ["logs.retentionDays"] = 7, ["logs.writeFiles"] = true });
        using var negative = await PatchAsync(admin, "server", new() { ["logs.retentionDays"] = -1 });
        using var inLibrary = await PatchAsync(admin, "tenant", new() { ["logs.retentionDays"] = 3 });
        var page = await SearchAsync(admin, "");

        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode, await saved.Content.ReadAsStringAsync(TestContext.CancellationToken));
        Assert.AreEqual(HttpStatusCode.BadRequest, negative.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, inLibrary.StatusCode, "Only the whole server sets them");
        Assert.AreEqual(7, page.RetentionDays);
        Assert.IsTrue(page.WriteFiles);
        Assert.AreEqual(SettingLevel.Server, SettingCatalog.Find("logs.writeFiles")!.Scope);
        Assert.AreEqual(Rights.LogSettings, SettingCatalog.Find("logs.retentionDays")!.Right);
    }

    [TestMethod]
    public async Task SecurityEventsAreKeptAsSecurityWarnings()
    {
        var factory = Factory(settings => settings["Sfe:Logs:WriteFiles"] = "true");
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var log = factory.Services.GetRequiredService<AuditLogService>();
        var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger(Category);
        logger.LogWarning("probe plain warning");
        logger.LogSecurityWarning("probe security {What}", "event");
        // A wrong password and a wrong setup code, through the API.
        using (var visitor = ApiClients.CreateFrom(factory, "203.0.113.50"))
        {
            (await visitor.PostAsJsonAsync("/api/session/login", new SignInRequest(ApiClients.AdminName, "wrong-password"), TestContext.CancellationToken)).Dispose();
            (await visitor.PostAsJsonAsync("/api/migration", new Sfe.Api.Endpoints.MigrationInput("not-the-code"), TestContext.CancellationToken)).Dispose();
        }
        await log.FlushAsync(cancellationToken: TestContext.CancellationToken);

        var probes = await SearchAsync(admin, "q=probe");
        var security = await SearchAsync(admin, "levels=securityWarning");
        var warnings = await SearchAsync(admin, "q=probe&levels=warning");
        var file = await File.ReadAllTextAsync(Path.Combine(_library.LogsPath, AuditLogService.FileName(DateOnly.FromDateTime(DateTime.UtcNow))),
            TestContext.CancellationToken);

        Assert.AreEqual(LogEntryLevel.SecurityWarning, probes.Entries.Single(e => e.Message == "probe security event").Level);
        Assert.AreEqual(LogEntryLevel.Warning, probes.Entries.Single(e => e.Message == "probe plain warning").Level);
        Assert.IsTrue(security.Entries.All(e => e.Level == LogEntryLevel.SecurityWarning));
        Assert.IsTrue(security.Entries.Any(e => e.Message.Contains("Sign-in refused for admin from 203.0.113.50: wrong name or password")),
            string.Join("\n", security.Entries.Select(e => e.Message)));
        Assert.IsTrue(security.Entries.Any(e => e.Message == "Update refused: wrong setup code"));
        Assert.AreEqual("probe plain warning", warnings.Entries.Single().Message, "Plain warnings are filtered apart");
        StringAssert.Contains(file, $" SEC {Category}: probe security event");
    }

    [TestMethod]
    public async Task TheSecurityLevelTravelsByName()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger(Category).LogSecurityWarning("probe by name");
        await FlushAsync(factory);

        var json = JsonNode.Parse(await admin.GetStringAsync("/api/logs?q=probe+by+name", TestContext.CancellationToken))!;

        Assert.AreEqual("securityWarning", json["entries"]![0]!["level"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task WritesDailyFilesWhenAskedAndForgetsOldEntriesAndFiles()
    {
        var factory = Factory(settings => settings["Sfe:Logs:WriteFiles"] = "true");
        await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var log = factory.Services.GetRequiredService<AuditLogService>();
        var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger(Category);
        logger.LogError(new InvalidOperationException("broken"), "file probe\nsecond line");
        await log.FlushAsync(cancellationToken: TestContext.CancellationToken);

        var today = Path.Combine(_library.LogsPath, AuditLogService.FileName(DateOnly.FromDateTime(DateTime.UtcNow)));
        var text = await File.ReadAllTextAsync(today, TestContext.CancellationToken);
        StringAssert.Contains(text, $" ERR {Category}: file probe\n    second line\n    System.InvalidOperationException: broken");

        var old = Path.Combine(_library.LogsPath, AuditLogService.FileName(new DateOnly(2020, 1, 1)));
        await File.WriteAllTextAsync(old, "old\n", TestContext.CancellationToken);
        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<AccessDbContext>>();
        await using (var db = await dbFactory.CreateDbContextAsync(TestContext.CancellationToken))
        {
            db.LogEntries.Add(new LogEntry { Timestamp = new DateTime(2020, 1, 1), Level = LogEntryLevel.Warning, Category = Category, Message = "old probe" });
            await db.SaveChangesAsync(TestContext.CancellationToken);
        }

        await log.PurgeAsync(TestContext.CancellationToken);

        Assert.IsFalse(File.Exists(old));
        Assert.IsTrue(File.Exists(today));
        await using (var db = await dbFactory.CreateDbContextAsync(TestContext.CancellationToken))
        {
            Assert.IsFalse(await db.LogEntries.AnyAsync(e => e.Message == "old probe", TestContext.CancellationToken));
            Assert.IsTrue(await db.LogEntries.AnyAsync(e => e.Message.StartsWith("file probe"), TestContext.CancellationToken));
        }
    }

    [TestMethod]
    public async Task WithoutFilesNothingIsWrittenToTheFolder()
    {
        var factory = Factory();
        await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger(Category).LogWarning("no file probe");
        await FlushAsync(factory);

        Assert.IsFalse(Directory.Exists(_library.LogsPath));
    }

    /// <summary>
    /// An install of 1.45.0 has no LogEntries table and its groups lack the rights. Until the update is confirmed the
    /// log writes nothing to the database: what was logged meanwhile waits, and is in the log after the update.
    /// </summary>
    [TestMethod]
    public async Task AnUpdatedInstallGetsTheTableTheRightsAndWhatWasLoggedMeanwhile()
    {
        await using (var factory = ApiClients.CreateFactory(_library))
            await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        await ExecuteAsync($"""
            DROP TABLE "LogEntries";
            DELETE FROM "GroupRights" WHERE "Right" IN ('{Rights.ViewLogs}', '{Rights.LogSettings}');
            UPDATE "Tenants" SET "RightsRevision" = 3;
            UPDATE "{AccessDbContext.ServerPropertiesTable}" SET "Value" = '1.45.0' WHERE "Key" = '{ServerProperty.VersionKey}';
            PRAGMA wal_checkpoint(TRUNCATE);
            """);

        var updated = Factory();
        _ = updated.Server;
        updated.Services.GetRequiredService<ILoggerFactory>().CreateLogger(Category).LogWarning("waiting probe");
        await updated.Services.GetRequiredService<AuditLogService>().FlushAsync(database: false, TestContext.CancellationToken);
        // Long enough for the worker to have flushed a few times.
        await Task.Delay(Sfe.Api.AuditLogWorker.FlushInterval * 2, TestContext.CancellationToken);
        var tableBefore = await TableExistsAsync("LogEntries");

        await ApiClients.MigrateAsync(updated, TestContext.CancellationToken);
        var admin = await ApiClients.SignInAsync(updated, ApiClients.AdminName, ApiClients.AdminPassword, TestContext.CancellationToken);
        _disposables.Add(admin);
        await FlushAsync(updated);
        var waiting = await SearchAsync(admin, "q=waiting+probe");
        var groups = await GetAsync<List<GroupInfo>>(admin, "/api/access/groups");

        Assert.IsFalse(tableBefore, "Nothing touched the database before the update");
        Assert.AreEqual("waiting probe", waiting.Entries.Single().Message);
        Assert.Contains(Rights.ViewLogs, groups.Single(g => g.BuiltIn == BuiltInGroup.Administrators).Rights);
        Assert.Contains(Rights.LogSettings, groups.Single(g => g.BuiltIn == BuiltInGroup.Administrators).Rights);
        Assert.DoesNotContain(Rights.ViewLogs, groups.Single(g => g.BuiltIn == BuiltInGroup.Users).Rights);
    }

    private WebApplicationFactory<Program> Factory(Action<IDictionary<string, string?>>? configure = null)
    {
        var factory = ApiClients.CreateFactory(_library, configure);
        _disposables.Add(factory);
        return factory;
    }

    private Task FlushAsync(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<AuditLogService>().FlushAsync(cancellationToken: TestContext.CancellationToken);

    private async Task<LogSearchResult> SearchAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync("/api/logs?" + query, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        return (await response.Content.ReadFromJsonAsync<LogSearchResult>(Json, TestContext.CancellationToken))!;
    }

    private Task<HttpResponseMessage> PatchAsync(HttpClient client, string level, Dictionary<string, JsonNode?> values) =>
        client.PatchAsJsonAsync($"/api/settings?level={level}", new SettingChanges(values), Json, TestContext.CancellationToken);

    private async Task<T> GetAsync<T>(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        return (await response.Content.ReadFromJsonAsync<T>(Json, TestContext.CancellationToken))!;
    }

    private async Task CreateUserAsync(HttpClient admin, UserInput input)
    {
        using var response = await admin.PostAsJsonAsync("/api/access/users", input, Json, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqliteConnection(_library.ConnectionString);
        await connection.OpenAsync(TestContext.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.CancellationToken);
    }

    private async Task<bool> TableExistsAsync(string table)
    {
        await using var connection = new SqliteConnection(_library.ConnectionString);
        await connection.OpenAsync(TestContext.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{table}'";
        return Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.CancellationToken)) > 0;
    }
}
