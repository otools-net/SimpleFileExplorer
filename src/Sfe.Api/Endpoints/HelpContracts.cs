namespace Otools.Net.Sfe.Api.Endpoints;

/// <param name="Pages">Every page of the help, in the order of their paths (`README.md` first in each folder).</param>
public sealed record HelpIndex(IReadOnlyList<HelpPage> Pages);

/// <param name="Path">The page's file under the help's root, with `/`: `user-guide/browsing.md`.</param>
/// <param name="Title">Its first level-one heading (its file name when it has none).</param>
/// <param name="Headings">Its headings of levels 2 and 3, in order, as plain text.</param>
public sealed record HelpPage(string Path, string Title, IReadOnlyList<HelpHeading> Headings);

/// <param name="Level">2 or 3.</param>
/// <param name="Text">The heading as it reads (code marks, emphasis and links taken out).</param>
public sealed record HelpHeading(int Level, string Text);
