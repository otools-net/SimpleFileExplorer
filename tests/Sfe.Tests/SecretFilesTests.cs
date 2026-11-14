using Otools.Net.Sfe.Core.Access;

namespace Otools.Net.Sfe.Tests;

/// <summary>The files and folders of the server's secrets (audit 2026-11, A9).</summary>
[TestClass]
public sealed class SecretFilesTests
{
    private const UnixFileMode Others =
        UnixFileMode.GroupRead
        | UnixFileMode.GroupWrite
        | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead
        | UnixFileMode.OtherWrite
        | UnixFileMode.OtherExecute;

    private string _folder = null!;

    [TestInitialize]
    public void Initialize()
    {
        _folder = Path.Combine(Path.GetTempPath(), "sfe-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_folder, recursive: true);

    [TestMethod]
    public void ASecretFileHoldsItsTextAndReplacesTheOneBefore()
    {
        var file = Path.Combine(_folder, "code.txt");

        SecretFiles.WriteAllText(file, "a first, longer secret");
        SecretFiles.WriteAllText(file, "second");

        Assert.AreEqual("second", File.ReadAllText(file));
    }

    [TestMethod]
    public void ASecretFileIsForTheServersAccountOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("No file modes on Windows: a file has the rights of its folder.");
            return;
        }
        var file = Path.Combine(_folder, "code.txt");

        SecretFiles.WriteAllText(file, "secret");

        Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
    }

    [TestMethod]
    public void AFileThatWasOpenToOthersIsClosedWhenRewritten()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("No file modes on Windows.");
            return;
        }
        var file = Path.Combine(_folder, "code.txt");
        File.WriteAllText(file, "old");
        File.SetUnixFileMode(
            file,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead
        );

        SecretFiles.WriteAllText(file, "new");

        Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
    }

    [TestMethod]
    public void ANewFolderIsPrivateWithThoseCreatedAboveIt()
    {
        var keys = Path.Combine(_folder, "data", "keys");

        SecretFiles.CreatePrivateDirectory(keys);

        Assert.IsTrue(Directory.Exists(keys));
        Assert.IsFalse(SecretFiles.IsOpenToOthers(keys));
        Assert.IsFalse(SecretFiles.IsOpenToOthers(Path.GetDirectoryName(keys)!));
        if (!OperatingSystem.IsWindows())
            Assert.AreEqual((UnixFileMode)0, File.GetUnixFileMode(keys) & Others);
    }

    [TestMethod]
    public void AnExistingFolderKeepsItsModeAndIsReportedOpen()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("No file modes on Windows.");
            return;
        }
        var shared = Path.Combine(_folder, "shared");
        var mode =
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead
            | UnixFileMode.GroupExecute;
        Directory.CreateDirectory(shared, mode);
        File.SetUnixFileMode(shared, mode);

        SecretFiles.CreatePrivateDirectory(shared);

        Assert.AreEqual(mode, File.GetUnixFileMode(shared), "Its administrator may have shared it on purpose");
        Assert.IsTrue(SecretFiles.IsOpenToOthers(shared));
    }

    [TestMethod]
    public void AMissingFolderIsNotOpen()
    {
        Assert.IsFalse(SecretFiles.IsOpenToOthers(Path.Combine(_folder, "missing")));
    }
}
