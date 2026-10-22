using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sfe.Core.Files;
using Sfe.Core.KeepAlive;
using Sfe.Core.Options;
using Sfe.Core.Paths;

namespace Sfe.Tests;

[TestClass]
public sealed class KeepAliveTests
{
    private const string MountInfo = """
        22 1 8:1 / / rw,relatime shared:1 - ext4 /dev/sda1 rw
        25 22 0:22 / /proc rw,nosuid shared:12 - proc proc rw
        40 22 0:35 / /mnt/nas rw,relatime shared:20 - cifs //nas/media rw,vers=3.1.1
        41 40 8:2 / /mnt/nas/local rw,relatime shared:21 - ext4 /dev/sdb1 rw
        42 22 0:36 / /mnt/nas2 rw,relatime shared:22 - nfs4 nas:/export rw
        43 22 0:37 / /srv/My\040Videos rw,relatime shared:23 - fuse.sshfs user@host:/videos rw
        44 22 8:3 / /mnt/stacked rw shared:24 - ext4 /dev/sdc1 rw
        45 22 0:38 / /mnt/stacked rw shared:25 - smb3 //nas/stacked rw
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "sfe-keepalive-" + Guid.NewGuid().ToString("N"));
    private readonly Settings _settings = new();
    private readonly RecordingLog _log = new();

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void SetUp() => Directory.CreateDirectory(_root);

    [TestCleanup]
    public void CleanUp() => Directory.Delete(_root, recursive: true);

    [TestMethod]
    [DataRow("/", "ext4")]
    [DataRow("/home/me/Videos", "ext4")]
    [DataRow("/mnt/nas", "cifs")]
    [DataRow("/mnt/nas/Movies/2020", "cifs")]
    [DataRow("/mnt/nas/local/x", "ext4")]
    [DataRow("/mnt/nas2/x", "nfs4")]
    [DataRow("/mnt/nas22", "ext4")]
    [DataRow("/srv/My Videos/a", "fuse.sshfs")]
    [DataRow("/mnt/stacked/a", "smb3")]
    public void FindsTheFileSystemOfTheDeepestMount(string path, string type) =>
        Assert.AreEqual(type, NetworkFolders.FileSystemTypeAt(MountInfo.ReplaceLineEndings("\n"), path));

    [TestMethod]
    [DataRow(@"\\nas\media\Movies", true)]
    [DataRow(@"\\?\UNC\nas\media", true)]
    [DataRow(@"\\?\C:\Media", false)]
    [DataRow(@"\\.\PhysicalDrive0", false)]
    [DataRow(@"C:\Media", false)]
    [DataRow("/mnt/nas", false)]
    public void RecognisesUncPaths(string path, bool unc) => Assert.AreEqual(unc, NetworkFolders.IsUncPath(path));

    [TestMethod]
    public void ALocalFolderIsNotOnTheNetwork() => Assert.IsFalse(NetworkFolders.IsNetwork(_root));

    [TestMethod]
    public async Task OffPingsNothing()
    {
        var (service, pings) = Create(KeepAliveMode.Off);

        await service.PingAllAsync(TestContext.CancellationToken);

        Assert.IsEmpty(pings);
        Assert.IsEmpty(service.Status());
    }

    [TestMethod]
    public async Task AlwaysPingsTheRootEachRound()
    {
        var (service, pings) = Create(KeepAliveMode.Always);

        await service.PingAllAsync(TestContext.CancellationToken);
        await service.PingAllAsync(TestContext.CancellationToken);

        CollectionAssert.AreEqual(new[] { Root, Root }, pings.ToList());
        var status = service.Status().Single();
        Assert.IsNotNull(status.LastPing);
        Assert.IsNull(status.Error);
        Assert.AreEqual(1, _log.Count("pinging"), _log.ToString());
    }

    [TestMethod]
    public async Task AutoLeavesALocalFolderAlone()
    {
        var (service, pings) = Create(KeepAliveMode.Auto, network: false);

        await service.PingAllAsync(TestContext.CancellationToken);
        await service.PingAllAsync(TestContext.CancellationToken);

        Assert.IsEmpty(pings);
        Assert.IsFalse(service.Status().Single().IsNetwork);
        Assert.AreEqual(1, _log.Count("is not on the network"), _log.ToString());
    }

    [TestMethod]
    public async Task AutoPingsAFolderOnTheNetwork()
    {
        var (service, pings) = Create(KeepAliveMode.Auto, network: true);

        await service.PingAllAsync(TestContext.CancellationToken);

        Assert.HasCount(1, pings);
        Assert.IsTrue(service.Status().Single().IsNetwork);
    }

    [TestMethod]
    public async Task AFailingFolderIsLoggedOnceThenWhenItAnswersAgain()
    {
        var failing = true;
        var (service, _) = Create(KeepAliveMode.Always, ping: _ => failing ? throw new IOException("The network name cannot be found.") : Task.CompletedTask);

        await service.PingAllAsync(TestContext.CancellationToken);
        await service.PingAllAsync(TestContext.CancellationToken);
        var status = service.Status().Single();
        Assert.AreEqual("The network name cannot be found.", status.Error);
        Assert.IsNotNull(status.FailingSince);
        Assert.AreEqual(1, _log.Count("does not answer"), _log.ToString());

        failing = false;
        await service.PingAllAsync(TestContext.CancellationToken);
        status = service.Status().Single();
        Assert.IsNull(status.Error);
        Assert.IsNull(status.FailingSince);
        Assert.AreEqual(1, _log.Count("answers again"), _log.ToString());
    }

    [TestMethod]
    public async Task ABlockedPingTimesOutAndIsNeverStackedUp()
    {
        var blocked = new TaskCompletionSource();
        var calls = 0;
        _settings.Value.KeepAlive.TimeoutSeconds = 1;
        var (service, _) = Create(KeepAliveMode.Always, ping: _ =>
        {
            Interlocked.Increment(ref calls);
            return blocked.Task;
        });

        await service.PingAllAsync(TestContext.CancellationToken);
        var status = service.Status().Single();
        StringAssert.Contains(status.Error, "no answer within 1 s");
        Assert.IsTrue(status.Waiting);

        // Still blocked: no second ping, no second warning.
        await service.PingAllAsync(TestContext.CancellationToken);
        Assert.AreEqual(1, calls);
        Assert.AreEqual(1, _log.Count("does not answer"), _log.ToString());

        blocked.SetResult();
        await Task.Delay(50, TestContext.CancellationToken);
        await service.PingAllAsync(TestContext.CancellationToken);
        Assert.AreEqual(2, calls);
        Assert.IsNull(service.Status().Single().Error);
        Assert.AreEqual(1, _log.Count("answers again"), _log.ToString());
    }

    [TestMethod]
    public async Task TurningItOnWakesTheLoopAtOnce()
    {
        var (service, pings) = Create(KeepAliveMode.Off);
        _settings.Value.KeepAlive.IntervalSeconds = KeepAliveOptions.MaxIntervalSeconds;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        var loop = service.RunAsync(stop.Token);

        _settings.Change(o => o.KeepAlive.Mode = KeepAliveMode.Always);
        for (var i = 0; i < 100 && pings.IsEmpty; i++)
            await Task.Delay(50, TestContext.CancellationToken);

        Assert.HasCount(1, pings);
        stop.Cancel();
        await loop;
    }

    [TestMethod]
    public async Task TheRealPingListsTheFolder()
    {
        _settings.Value.KeepAlive.Mode = KeepAliveMode.Always;
        using var service = New();

        await service.PingAllAsync(TestContext.CancellationToken);

        Assert.IsNull(service.Status().Single().Error, _log.ToString());
    }

    [TestMethod]
    public async Task MoreFoldersArePingedAndLeaveTheStatusWhenTakenOff()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Linked"));
        _settings.Value.KeepAlive.Folders = ["Linked", "../Outside", ".sfe", "", "/"];
        var (service, pings) = Create(KeepAliveMode.Always);

        await service.PingAllAsync(TestContext.CancellationToken);
        await service.PingAllAsync(TestContext.CancellationToken);

        var linked = Path.Combine(Root, "Linked");
        CollectionAssert.AreEquivalent(new[] { Root, linked, Root, linked }, pings.ToList(), string.Join(", ", pings));
        CollectionAssert.AreEqual(new[] { (Root, true), (linked, false) }, service.Status().Select(f => (f.Path, f.IsRoot)).ToList());
        Assert.AreEqual(1, _log.Count("“../Outside” is not a folder of the library"), _log.ToString());
        Assert.AreEqual(1, _log.Count("“.sfe” is not a folder of the library"), _log.ToString());

        _settings.Change(o => o.KeepAlive.Folders = []);
        await service.PingAllAsync(TestContext.CancellationToken);
        Assert.AreEqual(Root, service.Status().Single().Path);
    }

    [TestMethod]
    public async Task AutoLooksAtEachFolderOnItsOwn()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Linked"));
        _settings.Value.KeepAlive.Folders = ["Linked"];
        var (service, pings) = Create(KeepAliveMode.Auto);
        service.IsNetwork = path => path.EndsWith("Linked", StringComparison.Ordinal);

        await service.PingAllAsync(TestContext.CancellationToken);

        CollectionAssert.AreEqual(new[] { Path.Combine(Root, "Linked") }, pings.ToList());
        CollectionAssert.AreEqual(new bool?[] { false, true }, service.Status().Select(f => f.IsNetwork).ToList());
    }

    [TestMethod]
    public async Task TheWritePingWritesAndDeletesAFileInTheWorkFolder()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Linked"));
        _settings.Value.KeepAlive.Folders = ["Linked"];
        _settings.Value.KeepAlive.Method = KeepAliveMethod.Touch;
        var (service, pings) = Create(KeepAliveMode.Always);

        await service.PingAllAsync(TestContext.CancellationToken);

        // The library folder is written (not read); more folders are still read.
        var work = Path.Combine(_root, WorkFolder.Name, KeepAliveService.WorkPart);
        Assert.IsTrue(Directory.Exists(work), _log.ToString());
        Assert.IsEmpty(Directory.GetFileSystemEntries(work));
        CollectionAssert.AreEqual(new[] { Path.Combine(Root, "Linked") }, pings.ToList());
        CollectionAssert.AreEqual(new KeepAliveMethod?[] { KeepAliveMethod.Touch, KeepAliveMethod.List }, service.Status().Select(f => f.Method).ToList());
        Assert.IsTrue(service.Status().All(f => f.Error is null), _log.ToString());
    }

    [TestMethod]
    public async Task TheWritePingReadsWhenTheWorkFolderIsOnTheServer()
    {
        var cache = Path.Combine(_root, "..", Path.GetFileName(_root) + "-cache");
        _settings.Value.Thumbnails.CacheDirectory = cache;
        _settings.Value.Files.WorkFolder = WorkFolderLocation.Server;
        _settings.Value.KeepAlive.Method = KeepAliveMethod.Touch;
        var (service, pings) = Create(KeepAliveMode.Always);

        await service.PingAllAsync(TestContext.CancellationToken);
        await service.PingAllAsync(TestContext.CancellationToken);

        CollectionAssert.AreEqual(new[] { Root, Root }, pings.ToList());
        Assert.AreEqual(KeepAliveMethod.List, service.Status().Single().Method);
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, WorkFolder.Name)));
        Assert.IsFalse(Directory.Exists(cache));
        Assert.AreEqual(1, _log.Count("is read instead of written"), _log.ToString());
    }

    /// <summary>A service whose read pings are recorded (and answered by <paramref name="ping"/>); the write ping is real.</summary>
    private (KeepAliveService Service, ConcurrentQueue<string> Pings) Create(KeepAliveMode mode, bool network = true, Func<string, Task>? ping = null)
    {
        _settings.Value.KeepAlive.Mode = mode;
        var pings = new ConcurrentQueue<string>();
        var service = New();
        service.IsNetwork = _ => network;
        service.Ping = path =>
        {
            pings.Enqueue(path);
            return ping?.Invoke(path) ?? Task.CompletedTask;
        };
        return (service, pings);
    }

    private KeepAliveService New()
    {
        var paths = new LibraryPaths(_root);
        return new KeepAliveService(paths, new WorkFolder(paths, _settings), _settings, _log);
    }

    private string Root => Path.TrimEndingDirectorySeparator(new LibraryPaths(_root).Root);

    private sealed class Settings : IOptionsMonitor<SfeOptions>
    {
        private readonly List<Action<SfeOptions, string?>> _listeners = [];

        public SfeOptions Value { get; } = new();

        public SfeOptions CurrentValue => Value;

        public SfeOptions Get(string? name) => Value;

        public void Change(Action<SfeOptions> change)
        {
            change(Value);
            foreach (var listener in _listeners.ToList())
                listener(Value, null);
        }

        public IDisposable OnChange(Action<SfeOptions, string?> listener)
        {
            _listeners.Add(listener);
            return new Subscription(() => _listeners.Remove(listener));
        }

        private sealed class Subscription(Action remove) : IDisposable
        {
            public void Dispose() => remove();
        }
    }

    /// <summary>Every line keep alive logs (information and above).</summary>
    private sealed class RecordingLog : ILogger<KeepAliveService>
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public int Count(string text) => _lines.Count(l => l.Contains(text, StringComparison.Ordinal));

        public override string ToString() => string.Join(Environment.NewLine, _lines);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                _lines.Enqueue($"{logLevel}: {formatter(state, exception)}");
        }
    }
}
