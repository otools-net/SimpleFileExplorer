using Sfe.Api.Security;

namespace Sfe.Tests;

/// <summary>Archives' passwords held behind unlocks (<see cref="ArchiveUnlocks"/>): for their owner, their archive, a while.</summary>
[TestClass]
public sealed class ArchiveUnlocksTests
{
    private string _folder = "";
    private string _archive = "";

    [TestInitialize]
    public void Create()
    {
        _folder = Path.Combine(Path.GetTempPath(), "sfe-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _archive = Path.Combine(_folder, "a.zip");
        File.WriteAllText(_archive, "an archive");
    }

    [TestCleanup]
    public void Delete() => Directory.Delete(_folder, recursive: true);

    [TestMethod]
    public void AnUnlockServesItsOwnerItsLibraryAndItsArchiveOnly()
    {
        var unlocks = new ArchiveUnlocks(TimeProvider.System);
        var id = unlocks.Add(1, "user:1:stamp", _archive, "secret");

        Assert.AreEqual(64, id.Length);
        Assert.AreEqual("secret", unlocks.PasswordFor(id, 1, "user:1:stamp", _archive));
        Assert.IsNull(unlocks.PasswordFor(id, 2, "user:1:stamp", _archive), "another library");
        Assert.IsNull(unlocks.PasswordFor(id, 1, "user:2:stamp", _archive), "another account");
        Assert.IsNull(unlocks.PasswordFor(id, 1, "user:1:new stamp", _archive), "the account's password changed");
        var other = Path.Combine(_folder, "b.zip");
        File.WriteAllText(other, "another");
        Assert.IsNull(unlocks.PasswordFor(id, 1, "user:1:stamp", other), "another archive");
        Assert.IsNull(unlocks.PasswordFor(id[..^1] + (id[^1] == '0' ? "1" : "0"), 1, "user:1:stamp", _archive), "another id");
        Assert.IsNull(unlocks.PasswordFor(null, 1, "user:1:stamp", _archive));

        File.SetLastWriteTimeUtc(_archive, DateTime.UtcNow.AddHours(1));
        Assert.IsNull(unlocks.PasswordFor(id, 1, "user:1:stamp", _archive), "the archive changed");
    }

    [TestMethod]
    public void AnUnlockEndsUnusedOrSignedOut()
    {
        var time = new ShiftedTime();
        var unlocks = new ArchiveUnlocks(time);
        var id = unlocks.Add(1, "user:1:stamp", _archive, "secret");

        time.Offset = ArchiveUnlocks.Idle - TimeSpan.FromMinutes(1);
        Assert.AreEqual("secret", unlocks.PasswordFor(id, 1, "user:1:stamp", _archive), "used: kept another while");
        time.Offset += ArchiveUnlocks.Idle - TimeSpan.FromMinutes(1);
        Assert.AreEqual("secret", unlocks.PasswordFor(id, 1, "user:1:stamp", _archive));
        time.Offset += ArchiveUnlocks.Idle + TimeSpan.FromSeconds(1);
        Assert.IsNull(unlocks.PasswordFor(id, 1, "user:1:stamp", _archive), "unused too long");

        var again = unlocks.Add(1, "user:1:stamp", _archive, "secret");
        var someoneElse = unlocks.Add(1, "user:2:stamp", _archive, "secret");
        unlocks.Forget("user:1:stamp");
        Assert.IsNull(unlocks.PasswordFor(again, 1, "user:1:stamp", _archive), "signed out");
        Assert.AreEqual("secret", unlocks.PasswordFor(someoneElse, 1, "user:2:stamp", _archive));
    }

    [TestMethod]
    public void OnePersonKeepsAFewUnlocksTheOldestGoing()
    {
        var unlocks = new ArchiveUnlocks(TimeProvider.System);
        var ids = Enumerable.Range(0, ArchiveUnlocks.MaxPerOwner + 1).Select(_ => unlocks.Add(1, "user:1:s", _archive, "p")).ToList();

        Assert.IsNull(unlocks.PasswordFor(ids[0], 1, "user:1:s", _archive));
        Assert.AreEqual("p", unlocks.PasswordFor(ids[^1], 1, "user:1:s", _archive));
    }

    private sealed class ShiftedTime : TimeProvider
    {
        public TimeSpan Offset { get; set; }

        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + Offset;
    }
}
