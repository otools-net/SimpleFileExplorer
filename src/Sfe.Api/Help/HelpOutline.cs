using System.Text.RegularExpressions;
using Otools.Net.Sfe.Api.Endpoints;

namespace Otools.Net.Sfe.Api.Help;

/// <summary>
/// The title and headings of a Markdown page (ATX headings, <c>#</c> to <c>###</c>, outside code fences), as plain
/// text: what the help's search looks through.
/// </summary>
public static partial class HelpOutline
{
    public static (string? Title, IReadOnlyList<HelpHeading> Headings) Read(string markdown)
    {
        string? title = null;
        var headings = new List<HelpHeading>();
        string? fence = null;
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (Fence().Match(line) is { Success: true } opening)
            {
                var mark = opening.Groups[1].Value;
                if (fence is null)
                    fence = mark;
                else if (mark[0] == fence[0] && mark.Length >= fence.Length)
                    fence = null;
                continue;
            }

            if (fence is not null || Heading().Match(line) is not { Success: true } heading)
                continue;
            var level = heading.Groups[1].Length;
            var text = PlainText(heading.Groups[2].Value);
            if (level == 1)
                title ??= text;
            else if (text.Length > 0)
                headings.Add(new HelpHeading(level, text));
        }
        return (title, headings);
    }

    /// <summary>
    /// A heading's text without its Markdown: links keep their words, code and emphasis lose their marks.
    /// </summary>
    public static string PlainText(string heading)
    {
        var text = Link().Replace(heading, "$1");
        text = text.Replace("`", "").Replace("**", "").Replace("__", "");
        return ClosingHashes().Replace(text, "").Trim();
    }

    [GeneratedRegex(@"^ {0,3}(`{3,}|~{3,})")]
    private static partial Regex Fence();

    [GeneratedRegex(@"^ {0,3}(#{1,3})[ \t]+(.+)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"!?\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"[ \t]+#+[ \t]*$")]
    private static partial Regex ClosingHashes();
}
