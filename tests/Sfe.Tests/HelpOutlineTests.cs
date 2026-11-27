using Otools.Net.Sfe.Api.Endpoints;
using Otools.Net.Sfe.Api.Help;

namespace Otools.Net.Sfe.Tests;

/// <summary>The title and headings the help's search looks through (<see cref="HelpOutline"/>).</summary>
[TestClass]
public sealed class HelpOutlineTests
{
    [TestMethod]
    public void TheTitleIsTheFirstLevelOneHeadingAndHeadingsAreLevelsTwoAndThree()
    {
        var (title, headings) = HelpOutline.Read(
            "# Browsing\r\n\r\nText.\n\n## Layouts\n### Grid ##\n#### Deeper\n# Other"
        );

        Assert.AreEqual("Browsing", title);
        CollectionAssert.AreEqual(
            new[] { new HelpHeading(2, "Layouts"), new HelpHeading(3, "Grid") },
            headings.ToArray()
        );
    }

    [TestMethod]
    public void LinesInCodeFencesAreNotHeadings()
    {
        var (_, headings) = HelpOutline.Read(
            "## Before\n```sh\n# a comment\n~~~\n## still code\n```\n~~~~\n## code too\n~~~~\n## After\n"
        );

        CollectionAssert.AreEqual(new[] { "Before", "After" }, headings.Select(heading => heading.Text).ToArray());
    }

    [TestMethod]
    public void MarkdownIsTakenOutOfTheText()
    {
        Assert.AreEqual(
            "The Sfe:Root setting and its page",
            HelpOutline.PlainText("The `Sfe:Root` **setting** and [its page](../x.md)")
        );
    }

    [TestMethod]
    public void APageWithoutATitleHasNone()
    {
        Assert.IsNull(HelpOutline.Read("Text only\n#hashtag\n").Title);
    }
}
