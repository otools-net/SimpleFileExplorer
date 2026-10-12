using Microsoft.EntityFrameworkCore;
using Sfe.Core.Data;
using Sfe.Core.Indexing;
using Sfe.Core.Tasks;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>Schedules, run history and the index maintenance of the "Background tasks" dialog.</summary>
[TestClass]
public sealed class TaskTests
{
    private static readonly TimeZoneInfo PlusTwo = TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");

    private TestLibrary _library = null!;
    private CoreServices _services = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _services = await CoreServices.CreateAsync(_library);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _services.DisposeAsync();
        _library.Dispose();
    }

    private static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0) => new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    [TestMethod]
    public void OnceRunsAtItsStartOnly()
    {
        var start = Utc(2026, 10, 10, 12);
        Assert.AreEqual(start, ScheduleCalculator.Next(ScheduleKind.Once, start, 0, 0, Utc(2026, 10, 10, 11), PlusTwo));
        Assert.IsNull(ScheduleCalculator.Next(ScheduleKind.Once, start, 0, 0, start, PlusTwo));
    }

    [TestMethod]
    public void IntervalCountsFromItsStart()
    {
        var start = Utc(2026, 10, 10, 3);
        Assert.AreEqual(start, ScheduleCalculator.Next(ScheduleKind.Interval, start, 360, 0, Utc(2026, 10, 9), PlusTwo));
        // Strictly after: at 09:00 the next one is 15:00.
        Assert.AreEqual(Utc(2026, 10, 10, 15), ScheduleCalculator.Next(ScheduleKind.Interval, start, 360, 0, Utc(2026, 10, 10, 9), PlusTwo));
        Assert.AreEqual(Utc(2026, 10, 10, 15), ScheduleCalculator.Next(ScheduleKind.Interval, start, 360, 0, Utc(2026, 10, 10, 10, 30), PlusTwo));
    }

    [TestMethod]
    public void WeeklyRunsOnItsDaysAtItsTimeOfDay()
    {
        // 03:00 at UTC+2 is 01:00 UTC. 10 October 2026 is a Saturday.
        var start = Utc(2026, 10, 1, 1);
        const int mondayAndSaturday = 1 << 1 | 1 << 6;
        var runs = ScheduleCalculator.NextRuns(ScheduleKind.Weekly, start, 0, mondayAndSaturday, Utc(2026, 10, 10, 2), PlusTwo, 3);
        CollectionAssert.AreEqual(new[] { Utc(2026, 10, 12, 1), Utc(2026, 10, 17, 1), Utc(2026, 10, 19, 1) }, runs);
        Assert.IsNull(ScheduleCalculator.Next(ScheduleKind.Weekly, start, 0, 0, start, PlusTwo));
    }

    [TestMethod]
    public void WeeklyKeepsTheWallClockTimeAcrossDaylightSaving()
    {
        var paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
        // 02:30 in Paris on 20 March 2026 (UTC+1); the clocks go forward at 02:00 on 29 March, so 02:30 doesn't exist.
        var start = Utc(2026, 3, 20, 1, 30);
        var runs = ScheduleCalculator.NextRuns(ScheduleKind.Weekly, start, 0, ScheduleCalculator.AllDays, Utc(2026, 3, 28, 12), paris, 3);
        CollectionAssert.AreEqual(new[]
        {
            Utc(2026, 3, 29, 1, 30),    // 03:30 summer time, the skipped half hour later
            Utc(2026, 3, 30, 0, 30),    // 02:30 summer time
            Utc(2026, 3, 31, 0, 30),
        }, runs);
    }

    [TestMethod]
    public async Task ScanRunIsRecordedWithItsLog()
    {
        var scan = _services.Get<ScanCoordinator>();
        Assert.IsTrue(scan.TryStart("Media", TaskTrigger.Manual));
        await scan.Completion!;

        var history = _services.Get<TaskHistory>();
        var page = await history.ListAsync(BackgroundTaskKind.Scan, null, null, TestContext.CancellationToken);
        var run = page.Runs.Single();
        Assert.AreEqual(TaskOutcome.Completed, run.Outcome);
        Assert.AreEqual("Media", run.Path);
        Assert.AreEqual(TaskTrigger.Manual, run.Trigger);
        Assert.IsNotNull(run.Finished);
        StringAssert.Contains(run.Summary, "files indexed");

        var details = await history.GetAsync(run.Id, TestContext.CancellationToken);
        Assert.IsNotNull(details);
        Assert.IsTrue(details.Log.Any(l => l.Message.StartsWith("Scan of", StringComparison.Ordinal) && l.Message.Contains("started")), "start logged");
        Assert.IsTrue(details.Log.Any(l => l.Message.Contains("Completed")), "outcome logged");

        var last = await history.LastRunsAsync(TestContext.CancellationToken);
        Assert.AreEqual(run.Id, last[BackgroundTaskKind.Scan].Id);
    }

    [TestMethod]
    public async Task OldRunsAreForgotten()
    {
        await using (var db = await CreateDbAsync())
        {
            db.TaskRuns.AddRange(
                new TaskRun { Task = BackgroundTaskKind.Scan, Started = DateTime.UtcNow.AddDays(-40), Finished = DateTime.UtcNow.AddDays(-40), Outcome = TaskOutcome.Completed },
                new TaskRun { Task = BackgroundTaskKind.Scan, Started = DateTime.UtcNow.AddDays(-2), Finished = DateTime.UtcNow.AddDays(-2), Outcome = TaskOutcome.Completed },
                new TaskRun { Task = BackgroundTaskKind.Trickplay, Started = DateTime.UtcNow.AddDays(-50), Outcome = TaskOutcome.Running });
            await db.SaveChangesAsync(TestContext.CancellationToken);
        }

        var history = _services.Get<TaskHistory>();
        await history.PurgeAsync(TestContext.CancellationToken);
        await history.RecoverAsync(TestContext.CancellationToken);

        await using var check = await CreateDbAsync();
        var runs = await check.TaskRuns.OrderBy(r => r.Id).ToListAsync(TestContext.CancellationToken);
        Assert.HasCount(2, runs);
        Assert.AreEqual(TaskOutcome.Completed, runs[0].Outcome);
        // Still marked running at start-up: the server stopped during the run.
        Assert.AreEqual(TaskOutcome.Interrupted, runs[1].Outcome);
    }

    [TestMethod]
    public async Task ScheduleStartsItsTaskWhenDue()
    {
        var schedules = _services.Get<TaskScheduleService>();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        var loop = Task.Run(() => schedules.RunAsync(stop.Token), TestContext.CancellationToken);

        var (view, errors) = await schedules.CreateAsync(
            new TaskScheduleInput(BackgroundTaskKind.Scan, "ChildDir", ScheduleKind.Once, DateTimeOffset.UtcNow.AddSeconds(1), 0, 0, true),
            TestContext.CancellationToken);
        Assert.IsNull(errors);
        Assert.IsNotNull(view!.NextRun);

        var history = _services.Get<TaskHistory>();
        TaskRunSummary? run = null;
        for (var i = 0; i < 100 && run is not { Outcome: TaskOutcome.Completed }; i++)
        {
            await Task.Delay(100, TestContext.CancellationToken);
            run = (await history.ListAsync(BackgroundTaskKind.Scan, null, null, TestContext.CancellationToken)).Runs.FirstOrDefault();
        }

        await stop.CancelAsync();
        await loop;
        Assert.IsNotNull(run, "the schedule started a scan");
        Assert.AreEqual(TaskTrigger.Schedule, run.Trigger);
        Assert.AreEqual(view.Id, run.ScheduleId);
        Assert.AreEqual("ChildDir", run.Path);

        // A one-off has no next run once it ran.
        var after = (await schedules.ListAsync(TestContext.CancellationToken)).Single();
        Assert.IsNull(after.NextRun);
        Assert.IsNotNull(after.LastRun);
    }

    [TestMethod]
    public async Task InvalidSchedulesAreRefused()
    {
        var schedules = _services.Get<TaskScheduleService>();
        var past = DateTimeOffset.UtcNow.AddHours(-1);

        var (_, errors) = await schedules.CreateAsync(new TaskScheduleInput(BackgroundTaskKind.Scan, null, ScheduleKind.Once, past, 0, 0, true), TestContext.CancellationToken);
        Assert.IsTrue(errors!.ContainsKey("start"));
        (_, errors) = await schedules.CreateAsync(new TaskScheduleInput(BackgroundTaskKind.Trickplay, null, ScheduleKind.Weekly, past, 0, 0, true), TestContext.CancellationToken);
        Assert.IsTrue(errors!.ContainsKey("days"));
        (_, errors) = await schedules.CreateAsync(new TaskScheduleInput(BackgroundTaskKind.Thumbnails, null, ScheduleKind.Interval, past, 60, 0, true), TestContext.CancellationToken);
        Assert.IsTrue(errors!.ContainsKey("task"));
        (_, errors) = await schedules.CreateAsync(new TaskScheduleInput(BackgroundTaskKind.Scan, "../outside", ScheduleKind.Interval, past, 0, 0, true), TestContext.CancellationToken);
        Assert.IsTrue(errors!.ContainsKey("path"));
        Assert.IsTrue(errors.ContainsKey("intervalMinutes"));

        // An interval started in the past is fine: it runs at its next occurrence.
        var (view, none) = await schedules.CreateAsync(new TaskScheduleInput(BackgroundTaskKind.SimilarVideos, "ignored", ScheduleKind.Interval, past, 60, 0, true), TestContext.CancellationToken);
        Assert.IsNull(none);
        Assert.IsNull(view!.Path);
        Assert.IsTrue(view.NextRun > DateTimeOffset.UtcNow && view.NextRun <= DateTimeOffset.UtcNow.AddHours(1));
    }

    [TestMethod]
    public async Task ExcludedFoldersAreNotIndexed()
    {
        await using var services = await CoreServices.CreateAsync(_library, settings => settings["Sfe:Scan:ExcludedFolders:0"] = "Media");
        var scan = services.Get<ScanCoordinator>();
        Assert.IsTrue(scan.TryStart(""));
        await scan.Completion!;

        await using var db = await CreateDbAsync();
        var media = _library.FullPath("Media");
        Assert.IsFalse(await db.Directories.AnyAsync(d => d.FullName == media, TestContext.CancellationToken));
        Assert.IsFalse(await db.Files.AnyAsync(f => f.FullName.StartsWith(media), TestContext.CancellationToken));
        Assert.IsTrue(await db.Directories.AnyAsync(d => d.FullName == _library.FullPath("ChildDir"), TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task FolderCanBeRemovedFromTheIndex()
    {
        var scan = _services.Get<ScanCoordinator>();
        Assert.IsTrue(scan.TryStart(""));
        await scan.Completion!;

        var indexer = _services.Get<DirectoryIndexer>();
        var media = _library.FullPath("Media");
        var before = await indexer.GetStatsAsync(media, TestContext.CancellationToken);
        Assert.IsTrue(before.Indexed);
        Assert.IsGreaterThan(0, before.Files);

        var removed = await scan.RemoveFromIndexAsync("Media", TestContext.CancellationToken);
        Assert.AreEqual(before, removed);

        var after = await indexer.GetStatsAsync(media, TestContext.CancellationToken);
        Assert.AreEqual(new IndexedFolderStats(false, 0, 0, 0), after);
        Assert.IsTrue(File.Exists(_library.FullPath("Media/Holiday 2020.mp4")), "the files stay on the disk");
        Assert.IsTrue((await indexer.GetStatsAsync(_library.FullPath("ChildDir"), TestContext.CancellationToken)).Indexed);
    }

    private async Task<SfeDbContext> CreateDbAsync() =>
        await _services.Get<IDbContextFactory<SfeDbContext>>().CreateDbContextAsync(TestContext.CancellationToken);
}
