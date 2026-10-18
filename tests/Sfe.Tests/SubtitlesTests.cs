using Sfe.Core.Files;

namespace Sfe.Tests;

/// <summary>The video a subtitle file belongs to (<see cref="Subtitles"/>).</summary>
[TestClass]
public sealed class SubtitlesTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void MarksAreTakenOffTheNameOneAtATime()
    {
        CollectionAssert.AreEqual(new[] { "Film.en.forced", "Film.en", "Film" }, Subtitles.CandidateNames("Film.en.forced.srt").ToArray());
        CollectionAssert.AreEqual(new[] { "Film" }, Subtitles.CandidateNames("Film.vtt").ToArray());
        CollectionAssert.AreEqual(new[] { "My.Holiday.2024", "My.Holiday", "My" }, Subtitles.CandidateNames("My.Holiday.2024.srt").ToArray());
        CollectionAssert.AreEqual(new[] { "Talk.a-very-long-mark-indeed" }, Subtitles.CandidateNames("Talk.a-very-long-mark-indeed.srt").ToArray(), "not a mark");
        CollectionAssert.AreEqual(new[] { ".hidden" }, Subtitles.CandidateNames(".hidden.srt").ToArray());
    }

    [TestMethod]
    public void TheClosestVideoNextToItIsFound()
    {
        var folder = Directory.CreateTempSubdirectory("sfe-subtitles-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "Film.mp4"), "");
            File.WriteAllText(Path.Combine(folder, "Film.en.MKV"), "");
            File.WriteAllText(Path.Combine(folder, "Other.txt"), "");

            Assert.AreEqual(Path.Combine(folder, "Film.en.MKV"), Subtitles.FindVideo(Path.Combine(folder, "Film.en.srt"), ["mkv", "mp4"]), "the closest name first, capitals too");
            Assert.AreEqual(Path.Combine(folder, "Film.mp4"), Subtitles.FindVideo(Path.Combine(folder, "Film.fr.srt"), ["mkv", "mp4"]));
            Assert.IsNull(Subtitles.FindVideo(Path.Combine(folder, "Other.srt"), ["mkv", "mp4"]));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
