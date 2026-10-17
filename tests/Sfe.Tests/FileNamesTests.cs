using Sfe.Core.Files;

namespace Sfe.Tests;

/// <summary>Names file management gives files and folders (<see cref="FileNames"/>), on Windows and Linux rules.</summary>
[TestClass]
public sealed class FileNamesTests
{
    [TestMethod]
    [DataRow("photo.jpg")]
    [DataRow("Photos 2024")]
    [DataRow(".hidden")]
    [DataRow("Ünïcödé — 写真")]
    [DataRow("a")]
    [DataRow("CONSOLE")]
    [DataRow("COM10")]
    public void AcceptsOrdinaryNames(string name) => Assert.IsNull(FileNames.Check(name, portable: true), name);

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(".")]
    [DataRow("..")]
    [DataRow("a/b")]
    [DataRow("../x")]
    [DataRow("nul\0byte")]
    [DataRow("line\nbreak")]
    [DataRow("tab\there")]
    public void RefusesWhatNoSystemAccepts(string? name)
    {
        Assert.IsNotNull(FileNames.Check(name, portable: false));
        Assert.IsNotNull(FileNames.Check(name, portable: true));
    }

    [TestMethod]
    [DataRow("a\\b")]
    [DataRow("a:b")]
    [DataRow("file::$DATA")]
    [DataRow("what?")]
    [DataRow("star*")]
    [DataRow("pipe|")]
    [DataRow("quote\"")]
    [DataRow("less<")]
    [DataRow("trailing.")]
    [DataRow("trailing ")]
    [DataRow("CON")]
    [DataRow("con")]
    [DataRow("NUL.txt")]
    [DataRow("nul.tar.gz")]
    [DataRow("aux .log")]
    [DataRow("COM1")]
    [DataRow("LPT9.doc")]
    [DataRow("COM¹")]
    [DataRow("lpt³.txt")]
    public void PortableNamesRefuseWhatWindowsRefuses(string name)
    {
        Assert.IsNotNull(FileNames.Check(name, portable: true), name);
        if (OperatingSystem.IsWindows())
            Assert.IsNotNull(FileNames.Check(name, portable: false), "Windows always refuses " + name);
        else
            Assert.IsNull(FileNames.Check(name, portable: false), "Linux accepts " + name + " without portable names");
    }

    [TestMethod]
    public void LongNamesAreMeasuredAsEachSystemStoresThem()
    {
        var ascii = new string('a', FileNames.MaxLength);
        // 128 × 2 bytes in UTF-8, but 128 UTF-16 units: fine on NTFS, too long for most Linux file systems.
        var accented = new string('é', 128);

        Assert.IsNull(FileNames.Check(ascii, portable: true));
        Assert.IsNotNull(FileNames.Check(ascii + "a", portable: true));
        Assert.IsNotNull(FileNames.Check(accented, portable: true), "Portable names fit Linux's 255 bytes");
        Assert.AreEqual(OperatingSystem.IsWindows(), FileNames.Check(accented, portable: false) is null);
    }

    [TestMethod]
    public void KeepBothNumbersTheCopyLikeExplorer()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "beach.jpg", "beach (2).jpg", "Photos", "Photos (2)", "Photos (3)" };

        Assert.AreEqual("beach (3).jpg", FileNames.KeepBoth("beach.jpg", isFolder: false, taken.Contains));
        Assert.AreEqual("Photos (4)", FileNames.KeepBoth("Photos", isFolder: true, taken.Contains));
        Assert.AreEqual("archive.tar (2).gz", FileNames.KeepBoth("archive.tar.gz", isFolder: false, taken.Contains));
        Assert.AreEqual("v1.2 (2)", FileNames.KeepBoth("v1.2", isFolder: true, taken.Contains), "A folder's dot is not an extension");
        Assert.AreEqual(".profile (2)", FileNames.KeepBoth(".profile", isFolder: false, taken.Contains));
        Assert.IsNull(FileNames.KeepBoth("x", isFolder: false, _ => true));
    }
}
