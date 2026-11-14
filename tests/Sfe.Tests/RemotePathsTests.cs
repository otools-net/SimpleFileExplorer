using Otools.Net.Sfe.Core.Connections;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// Paths inside a connection (<see cref="RemotePaths"/>): nothing a provider receives leaves its root.
/// </summary>
[TestClass]
public sealed class RemotePathsTests
{
    [TestMethod]
    [DataRow(null, "")]
    [DataRow("", "")]
    [DataRow("  ", "")]
    [DataRow("/", "")]
    [DataRow("Photos", "Photos")]
    [DataRow("/Photos/2024/", "Photos/2024")]
    [DataRow("Photos//2024", "Photos/2024")]
    [DataRow("a b/ünï — 写真.jpg", "a b/ünï — 写真.jpg")]
    [DataRow("C:/x", "C:/x")]
    [DataRow("...", "...")]
    public void NormalizesWhatStaysInside(string? path, string expected) =>
        Assert.AreEqual(expected, RemotePaths.Normalize(path));

    [TestMethod]
    [DataRow("..")]
    [DataRow("../x")]
    [DataRow("a/../b")]
    [DataRow("a/./b")]
    [DataRow("a/..")]
    [DataRow("a\\..\\b")]
    [DataRow("a\\b")]
    [DataRow("a/b\0c")]
    [DataRow("a/line\nbreak")]
    public void RefusesWhatCouldLeaveOrBeReadTwoWays(string path) => Assert.IsNull(RemotePaths.Normalize(path));

    [TestMethod]
    public void RefusesNamesAndPathsTooLong()
    {
        Assert.IsNull(RemotePaths.Normalize(new string('a', RemotePaths.MaxNameLength + 1)));
        var deep = string.Join('/', Enumerable.Repeat(new string('a', 200), 6));
        Assert.IsNull(RemotePaths.Normalize(deep));
        Assert.IsNotNull(RemotePaths.Normalize(new string('a', RemotePaths.MaxNameLength)));
    }

    [TestMethod]
    [DataRow("photo.jpg", true)]
    [DataRow(".hidden", true)]
    [DataRow("a b", true)]
    [DataRow("", false)]
    [DataRow(null, false)]
    [DataRow(".", false)]
    [DataRow("..", false)]
    [DataRow("a/b", false)]
    [DataRow("a\\b", false)]
    [DataRow("name.", false)]
    [DataRow("name ", false)]
    [DataRow(" name", false)]
    [DataRow("tab\there", false)]
    public void ChecksNamesOfNewItems(string? name, bool valid) =>
        Assert.AreEqual(valid, RemotePaths.IsValidName(name), name);

    [TestMethod]
    public void CombinesAndSplits()
    {
        Assert.AreEqual("a/b", RemotePaths.Combine("a", "b"));
        Assert.AreEqual("b", RemotePaths.Combine("", "b"));
        Assert.AreEqual("a", RemotePaths.Combine("a", ""));
        Assert.AreEqual("a/b", RemotePaths.Parent("a/b/c"));
        Assert.AreEqual("", RemotePaths.Parent("a"));
        Assert.AreEqual("c.txt", RemotePaths.NameOf("a/b/c.txt"));
        Assert.AreEqual("", RemotePaths.NameOf(""));
    }

    [TestMethod]
    public void TellsWhatIsInATreeByTheProvidersCase()
    {
        Assert.IsTrue(RemotePaths.IsInTree("Photos/2024", "Photos", StringComparison.Ordinal));
        Assert.IsTrue(RemotePaths.IsInTree("Photos", "Photos", StringComparison.Ordinal));
        Assert.IsTrue(RemotePaths.IsInTree("anything", "", StringComparison.Ordinal));
        // A name that only starts like the folder's is not in it.
        Assert.IsFalse(RemotePaths.IsInTree("Photos2/x", "Photos", StringComparison.Ordinal));
        Assert.IsFalse(RemotePaths.IsInTree("photos/x", "Photos", StringComparison.Ordinal));
        Assert.IsTrue(RemotePaths.IsInTree("photos/x", "Photos", StringComparison.OrdinalIgnoreCase));
    }
}
