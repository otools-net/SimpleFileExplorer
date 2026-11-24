using Microsoft.Extensions.Logging.Abstractions;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// The unfinished uploads into connections kept across restarts (<see cref="RemoteUploadRecords"/>): read back by a
/// new <see cref="RemoteUploads"/>, the session protected, records that can't be used removed, and the sweeps.
/// </summary>
[TestClass]
public sealed class RemoteUploadsTests
{
    private readonly SteppedClock _clock = new();
    private string _folder = null!;

    [TestInitialize]
    public void Initialize() =>
        _folder = Path.Combine(Path.GetTempPath(), "sfe-tests", Guid.NewGuid().ToString("N"), "uploads");

    [TestCleanup]
    public void Cleanup()
    {
        var parent = Path.GetDirectoryName(_folder)!;
        if (Directory.Exists(parent))
            Directory.Delete(parent, recursive: true);
    }

    [TestMethod]
    public void AnUploadIsReadBackWithItsSessionAndWhatTheProviderHas()
    {
        var first = Create();
        var upload = first.Add(Start(connection: 3, owner: 7), "session-abc");
        first.Advance(upload, 40);

        var again = Create().List(new RemoteUploadOwner(3, 1, 7)).Single();
        var stored = File.ReadAllText(Directory.GetFiles(_folder).Single());

        Assert.AreEqual(
            (upload.Id, "Trips", "a.txt", 100L, 40L, "session-abc"),
            (again.Id, again.Target, again.Name, again.Size, again.Received, again.Session)
        );
        Assert.AreEqual(upload.LastActivity, again.LastActivity);
        StringAssert.Contains(stored, FakeProtector.Prefix + "session-abc");
    }

    [TestMethod]
    public void AnUploadRemovedIsNotReadBack()
    {
        var first = Create();
        var kept = first.Add(Start(connection: 3, owner: 7), "session-1");
        first.Remove(first.Add(Start(connection: 3, owner: 7), "session-2"));

        var again = Create().List(new RemoteUploadOwner(3, 1, 7));

        Assert.AreEqual(kept.Id, again.Single().Id);
        Assert.HasCount(1, Directory.GetFiles(_folder));
    }

    [TestMethod]
    public void ARecordThatCannotBeUsedIsRemoved()
    {
        Create().Add(Start(connection: 3, owner: 7), "session-1");
        var record = Directory.GetFiles(_folder).Single();
        // Protected with other keys (a data folder restored without its keys folder).
        File.WriteAllText(record, File.ReadAllText(record).Replace(FakeProtector.Prefix, "other:"));
        File.WriteAllText(Path.Combine(_folder, "0123456789abcdef0123456789abcdef.json"), "not json");

        var again = Create().List(new RemoteUploadOwner(3, 1, 7));

        Assert.IsEmpty(again);
        Assert.IsEmpty(Directory.GetFiles(_folder));
    }

    [TestMethod]
    public void IdleUploadsAndThoseOfConnectionsRemovedAreSweptButNotOneInUse()
    {
        var uploads = Create();
        var idle = uploads.Add(Start(connection: 3, owner: 7), "session-idle");
        var busy = uploads.Add(Start(connection: 3, owner: 7), "session-busy");
        _clock.Now += RemoteUploads.IdleLimit + TimeSpan.FromMinutes(1);
        var fresh = uploads.Add(Start(connection: 3, owner: 7), "session-fresh");
        var removedConnection = uploads.Add(Start(connection: 9, owner: 7), "session-gone");
        busy.Gate.Wait();

        var idleRemoved = uploads.RemoveIdle();
        var goneRemoved = uploads.RemoveOfConnectionsNotIn(new HashSet<int> { 3 });
        busy.Gate.Release();

        Assert.AreEqual((1, 1), (idleRemoved, goneRemoved));
        var left = Directory.GetFiles(_folder).Select(Path.GetFileNameWithoutExtension).ToList();
        CollectionAssert.AreEquivalent(new[] { busy.Id, fresh.Id }, left);
        Assert.IsNull(uploads.Find(idle.Id, new RemoteUploadOwner(3, 1, 7)));
        Assert.IsNull(uploads.Find(removedConnection.Id, new RemoteUploadOwner(9, 1, 7)));
    }

    private RemoteUploads Create() =>
        new(_clock, new RemoteUploadRecords(_folder, new FakeProtector(), NullLogger<RemoteUploads>.Instance));

    private static RemoteUploadStart Start(int connection, int owner) =>
        new()
        {
            ConnectionId = connection,
            TenantId = 1,
            OwnerId = owner,
            OwnerName = "alice",
            Target = "Trips",
            Name = "a.txt",
            Size = 100,
        };

    private sealed class SteppedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
