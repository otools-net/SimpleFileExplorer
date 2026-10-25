using System.Text.Json;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>The committed test media and their list stay in step (tests/samples/README.md).</summary>
[TestClass]
public sealed class TestSamplesTests
{
    [TestMethod]
    public void EverySampleOfTheListIsCommittedAndNothingElse()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestSamples.Folder, "..", "media.json")));
        var listed = manifest.RootElement.GetProperty("media").EnumerateArray().Select(entry => entry.GetProperty("name").GetString()!).ToList();
        var files = Directory.EnumerateFiles(TestSamples.Folder).Select(Path.GetFileName).ToList();

        CollectionAssert.AllItemsAreUnique(listed);
        Assert.IsEmpty(listed.Except(files).ToList(),
            "Listed in tests/samples/media.json but not made: run node agents/tools/samples/update-samples.mjs --ffmpeg <path>");
        Assert.IsEmpty(files.Except(listed).ToList(),
            "In tests/samples/media but not in media.json: list it there (how ffmpeg makes it), or delete it");
    }
}
