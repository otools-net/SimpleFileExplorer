using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Otools.Net.Sfe.Core.Files;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// <see cref="DiskWrites"/>: whether the file system lets the server put files in a folder, asked without writing.
/// </summary>
[TestClass]
public sealed class DiskWritesTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"sfe-writes-{Guid.NewGuid():N}");

    [TestInitialize]
    public void Initialize() => Directory.CreateDirectory(_root);

    [TestCleanup]
    public void Cleanup()
    {
        if (OperatingSystem.IsWindows())
            AllowAdding(_root);
        else
            File.SetUnixFileMode(_root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public void AFolderOfTheServersOwnTakesFiles()
    {
        Assert.AreEqual(DiskWrite.Accepted, DiskWrites.Probe(_root));
        Assert.IsEmpty(Directory.GetFileSystemEntries(_root), "Nothing is written to tell");
    }

    [TestMethod]
    public void AFolderThatIsGoneIsNotSaidReadOnly() =>
        Assert.AreEqual(DiskWrite.Accepted, DiskWrites.Probe(Path.Combine(_root, "gone")));

    [TestMethod]
    public void AFolderTheServerMayNotAddToIsRefused()
    {
        if (OperatingSystem.IsWindows())
        {
            DenyAdding(_root);
        }
        else
        {
            if (Environment.UserName == "root")
                Assert.Inconclusive("root writes everywhere but on read-only mounts.");
            File.SetUnixFileMode(_root, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }

        Assert.AreEqual(DiskWrite.Refused, DiskWrites.Probe(_root));
    }

    [TestMethod]
    public void AnAnswerIsKeptForItsFolder()
    {
        var first = DiskWrites.Of(_root);
        if (OperatingSystem.IsWindows())
            DenyAdding(_root);
        else
            File.SetUnixFileMode(_root, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        Assert.AreEqual(DiskWrite.Accepted, first);
        Assert.AreEqual(first, DiskWrites.Of(_root), $"Kept {DiskWrites.KeptFor}");
    }

    [SupportedOSPlatform("windows")]
    private static void DenyAdding(string folder) => ChangeRule(folder, add: true);

    [SupportedOSPlatform("windows")]
    private static void AllowAdding(string folder) => ChangeRule(folder, add: false);

    [SupportedOSPlatform("windows")]
    private static void ChangeRule(string folder, bool add)
    {
        var info = new DirectoryInfo(folder);
        var security = info.GetAccessControl();
        var rule = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories,
            AccessControlType.Deny
        );
        if (add)
            security.AddAccessRule(rule);
        else
            security.RemoveAccessRule(rule);
        info.SetAccessControl(security);
    }
}
