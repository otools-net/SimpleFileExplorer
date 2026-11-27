using Otools.Net.Sfe.Core.Files;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// <see cref="DiskSpace.FreeBytes"/>, which copies, extractions, uploads and transfers check before writing: the
/// volume a folder is really on (through a junction or a link), measured on the closest folder that exists; and
/// <see cref="DiskSpace.VolumeFolder"/>, which the sidebar's bar measures for the library folder shown.
/// </summary>
[TestClass]
public sealed class DiskSpaceTests
{
    // Other tests write while these run: free space read twice may move a little.
    private const long Slack = 512L << 20;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"sfe-disk-{Guid.NewGuid():N}");

    [TestInitialize]
    public void Initialize() => Directory.CreateDirectory(_root);

    [TestCleanup]
    public void Cleanup()
    {
        // The links first, alone: never into the volume they lead to.
        var library = Path.Combine(_root, "library");
        var links = Directory
            .EnumerateDirectories(_root)
            .Concat(Directory.Exists(library) ? Directory.EnumerateDirectories(library) : [])
            .Where(d => new DirectoryInfo(d).LinkTarget is not null);
        foreach (var link in links.ToList())
            Directory.Delete(link);
        Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public void FoldersNotMadeYetAreMeasuredOnTheirClosestParent()
    {
        var free = DiskSpace.FreeBytes(Path.Combine(_root, "not", "made", "yet"));

        AssertClose(DiskSpace.Of(_root).Free, free);
    }

    [TestMethod]
    public void AFileIsMeasuredOnItsFolder()
    {
        var file = Path.Combine(_root, "a.txt");
        File.WriteAllText(file, "a");

        AssertClose(DiskSpace.Of(_root).Free, DiskSpace.FreeBytes(file));
    }

    [TestMethod]
    public void APathOfNoVolumeIsUnknown() => Assert.IsNull(DiskSpace.FreeBytes("\0"));

    [TestMethod]
    public void AJunctionIsMeasuredOnTheVolumeItLeadsTo()
    {
        var here = DiskSpace.Of(_root).Free!.Value;
        var other = DriveInfo
            .GetDrives()
            .FirstOrDefault(d =>
                d.IsReady
                && d.DriveType == DriveType.Fixed
                && Math.Abs(d.AvailableFreeSpace - here) > 4 * Slack
            );
        if (other is null)
            Assert.Inconclusive("No second volume whose free space differs from the temp folder's.");
        var link = Path.Combine(_root, "elsewhere");
        if (!TryLinkFolder(link, other.RootDirectory.FullName))
            Assert.Inconclusive("A junction or a link could not be made here.");

        AssertClose(other.AvailableFreeSpace, DiskSpace.FreeBytes(link));
        AssertClose(other.AvailableFreeSpace, DiskSpace.FreeBytes(Path.Combine(link, "not made yet")));
    }

    [TestMethod]
    public void AFolderBehindAJunctionIsOnTheJunctionsVolume()
    {
        var library = Directory.CreateDirectory(Path.Combine(_root, "library")).FullName;
        var target = Directory.CreateDirectory(Path.Combine(_root, "target", "deep")).FullName;
        var link = Path.Combine(library, "Movies");
        if (!TryLinkFolder(link, Path.GetDirectoryName(target)!))
            Assert.Inconclusive("A junction or a link could not be made here.");

        Assert.AreEqual(link, DiskSpace.VolumeFolder(library, link));
        Assert.AreEqual(link, DiskSpace.VolumeFolder(library, Path.Combine(link, "deep")));
        Assert.AreEqual(link, DiskSpace.VolumeFolder(library, Path.Combine(link, "deep", "not made yet")));
    }

    [TestMethod]
    public void AnOrdinaryFolderIsOnTheRootsVolume()
    {
        var library = Directory.CreateDirectory(Path.Combine(_root, "library")).FullName;
        var folder = Directory.CreateDirectory(Path.Combine(library, "a", "b")).FullName;

        Assert.AreEqual(library, DiskSpace.VolumeFolder(library + Path.DirectorySeparatorChar, folder));
        Assert.AreEqual(library, DiskSpace.VolumeFolder(library, library));
        Assert.AreEqual(library, DiskSpace.VolumeFolder(library, _root), "Outside the library: its root");
    }

    private static void AssertClose(long? expected, long? actual)
    {
        Assert.IsNotNull(expected);
        Assert.IsNotNull(actual);
        Assert.IsLessThan(Slack, Math.Abs(expected.Value - actual.Value), $"{expected} free, read {actual}");
    }

    /// <summary>A junction on Windows (no privilege needed), a symbolic link elsewhere.</summary>
    internal static bool TryLinkFolder(string link, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            using var process = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                }
            )!;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
