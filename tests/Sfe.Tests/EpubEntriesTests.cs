using System.IO.Compression;
using Sfe.Core.Files;

namespace Sfe.Tests;

/// <summary>The files inside an EPUB, as the reader asks for them (<see cref="EpubEntries"/>).</summary>
[TestClass]
public sealed class EpubEntriesTests
{
    [TestMethod]
    public void ABooksPathMakesOneUrlSegment()
    {
        foreach (var path in new[] { "Books/Trip.epub", "Libros/Año ñandú/Viaje.epub", "a?b/c+d/e.epub" })
        {
            var token = EpubEntries.Token(path);
            Assert.DoesNotContain("/", token, path);
            Assert.DoesNotContain("+", token, path);
            Assert.DoesNotContain("=", token, path);
            Assert.AreEqual(path, EpubEntries.PathOf(token));
        }
        Assert.IsNull(EpubEntries.PathOf("not a token!"));
        Assert.IsNull(EpubEntries.PathOf(""));
    }

    [TestMethod]
    public void EntriesAreFoundAsWrittenThenInAnyCase()
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("OEBPS/Images/Cover.jpg");
            zip.CreateEntry("OEBPS/images/cover.JPG");
            zip.CreateEntry("OEBPS/Text/");
        }
        memory.Position = 0;
        using var archive = new ZipArchive(memory, ZipArchiveMode.Read);

        Assert.AreEqual("OEBPS/images/cover.JPG", EpubEntries.Find(archive, "OEBPS/images/cover.JPG")?.FullName);
        Assert.AreEqual("OEBPS/Images/Cover.jpg", EpubEntries.Find(archive, "/oebps/IMAGES/cover.jpg")?.FullName, "the first in any case");
        Assert.IsNull(EpubEntries.Find(archive, "OEBPS/Text/"), "a folder");
        Assert.IsNull(EpubEntries.Find(archive, "OEBPS/none.css"));
        Assert.AreEqual("application/xhtml+xml", EpubEntries.ContentTypeOf("a/b.XHTML"));
        Assert.AreEqual("text/plain", EpubEntries.ContentTypeOf("script.js"), "never run");
        Assert.AreEqual("application/octet-stream", EpubEntries.ContentTypeOf("data.bin"));
    }
}
