using Sfe.Core.Paths;

namespace Sfe.Tests;

[TestClass]
public sealed class LibraryPathsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "sfe-root");
    private readonly LibraryPaths _paths = new(Root);

    [TestMethod]
    [DataRow(null, "")]
    [DataRow("", "")]
    [DataRow("Movies", "Movies")]
    [DataRow("/Movies/2020/", "Movies/2020")]
    [DataRow("Movies//2020", "Movies/2020")]
    public void ResolvesPathsInsideTheRoot(string? relative, string expected)
    {
        Assert.IsTrue(_paths.TryResolve(relative, out var full));
        Assert.AreEqual(expected, _paths.ToRelative(full));
    }

    [TestMethod]
    public void RootResolvesWithoutTrailingSeparator()
    {
        Assert.AreEqual(Path.TrimEndingDirectorySeparator(_paths.Root), _paths.Resolve(""));
        Assert.AreEqual(_paths.Resolve(""), new DirectoryInfo(_paths.Resolve("Movies")).Parent!.FullName);
    }

    [TestMethod]
    [DataRow("..")]
    [DataRow("Movies/../../etc")]
    [DataRow("./Movies")]
    [DataRow("/../etc")]
    public void RejectsPathsEscapingTheRoot(string relative)
    {
        Assert.IsFalse(_paths.TryResolve(relative, out _));
    }

    [TestMethod]
    [DataRow("C:/Windows")]
    [DataRow(@"C:\Windows")]
    [DataRow(@"..\..\Windows")]
    [DataRow("file.txt:stream")]
    public void RejectsDrivesAndStreamsOnWindows(string relative)
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Windows only.");

        Assert.IsFalse(_paths.TryResolve(relative, out _));
    }

    [TestMethod]
    public void BackslashSeparatesFoldersOnWindows()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Windows only.");

        Assert.AreEqual("Movies/2020", _paths.ToRelative(_paths.Resolve(@"Movies\2020")));
    }

    [TestMethod]
    [DataRow("Show: Part 1.mkv")]
    [DataRow(@"a\b.txt")]
    [DataRow(@"..\..\etc")]
    public void ColonsAndBackslashesAreFileNameCharactersOnUnix(string name)
    {
        if (OperatingSystem.IsWindows())
            Assert.Inconclusive("Unix only.");

        var full = _paths.Resolve("Movies/" + name);

        Assert.AreEqual(Path.Combine(Root, "Movies", name), full);
        Assert.AreEqual("Movies/" + name, _paths.ToRelative(full));
    }

    [TestMethod]
    public void PathsOutsideTheRootHaveNoRelativeForm()
    {
        Assert.IsNull(_paths.ToRelative(Path.Combine(Path.GetTempPath(), "sfe-root-sibling", "file.txt")));
    }

    [TestMethod]
    public void BareDriveLetterMeansDriveRoot()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Windows only.");

        Assert.AreEqual(@"V:\", new LibraryPaths("V:").Root);
    }

    [TestMethod]
    public void FileSystemRootIsALibrary()
    {
        if (OperatingSystem.IsWindows())
            Assert.Inconclusive("Unix only.");

        var paths = new LibraryPaths("/");

        Assert.AreEqual("/", paths.Root);
        Assert.AreEqual("/", paths.RootName);
        Assert.AreEqual("/", paths.Resolve(""));
        Assert.AreEqual("/srv/media", paths.Resolve("srv/media"));
        Assert.AreEqual("srv/media", paths.ToRelative("/srv/media"));
    }

    [TestMethod]
    public void TildeMeansHomeFolder()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.AreEqual(Path.TrimEndingDirectorySeparator(home), new LibraryPaths("~").Resolve(""));
        Assert.AreEqual(Path.Combine(home, "Videos"), new LibraryPaths("~/Videos").Resolve(""));
    }
}
