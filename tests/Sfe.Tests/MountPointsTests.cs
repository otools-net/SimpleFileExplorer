using Otools.Net.Sfe.Core.Files;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// <see cref="MountPoints"/>: the mount points of Linux's <c>/proc/self/mountinfo</c>, and the one holding a folder
/// (the storage bar measures it, agents/docs/features/storage-usage.md).
/// </summary>
[TestClass]
public sealed class MountPointsTests
{
    private const string MountInfo = """
        22 1 8:2 / / rw,relatime shared:1 - ext4 /dev/sda2 rw
        36 22 0:31 / /data rw,relatime shared:2 - ext4 /dev/sdb1 rw
        41 36 0:45 / /data/library/Old\040films rw - tmpfs tmpfs rw,size=65536k
        42 36 8:17 /exports/photos /data/library/Photos rw - ext4 /dev/sdc1 rw
        43 22 0:5 / /proc rw - proc proc rw
        """;

    private static readonly IReadOnlyList<string> Points = MountPoints.Parse(MountInfo);

    [TestMethod]
    public void TheMountPointsAreTheFifthFieldsDecoded() =>
        CollectionAssert.AreEqual(
            new[] { "/", "/data", "/data/library/Old films", "/data/library/Photos", "/proc" },
            Points.ToArray()
        );

    [TestMethod]
    public void AFolderIsHeldByTheLongestMountPointItIsIn()
    {
        Assert.AreEqual("/data/library/Photos", MountPoints.Holding("/data/library/Photos/2024/June", Points));
        Assert.AreEqual("/data/library/Old films", MountPoints.Holding("/data/library/Old films", Points));
        Assert.AreEqual("/data", MountPoints.Holding("/data/library/Photographs", Points), "Not a name's prefix");
        Assert.AreEqual("/", MountPoints.Holding("/home/me", Points));
    }

    [TestMethod]
    public void NoListGivesNothing() => Assert.IsNull(MountPoints.Holding("/data", null));

    [TestMethod]
    public void OnlyLinuxReadsTheList()
    {
        if (!OperatingSystem.IsLinux())
            Assert.IsNull(MountPoints.Holding(Path.GetTempPath()));
        else
            Assert.IsNotNull(MountPoints.Holding(Path.GetTempPath()));
    }
}
