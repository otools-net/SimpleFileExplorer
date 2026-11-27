using Otools.Net.Sfe.Core.Options;
using Otools.Net.Sfe.Core.Settings;

namespace Otools.Net.Sfe.Core.Tagging;

/// <summary>
/// What the sidebar shows (<see cref="TagService.GetSidebarAsync"/>); a list switched off isn't read.
/// </summary>
/// <param name="Count">Tags per list.</param>
/// <param name="History">Searched and viewed tags are recorded: off, those lists stay empty.</param>
/// <param name="Popular">The most used tags are shown.</param>
/// <param name="Searched">The last searched tags are shown (with <paramref name="History"/>).</param>
/// <param name="Lucky">Random other tags are shown.</param>
public sealed record TagSidebarChoice(int Count, bool History, bool Popular, bool Searched, bool Lucky)
{
    /// <summary>The library's settings.</summary>
    public static TagSidebarChoice From(TagOptions tags) =>
        new(tags.SidebarCount, tags.RecordHistory, tags.SidebarPopular, tags.SidebarSearched, tags.SidebarLucky);

    /// <summary>The settings a user works with (the library's, with the user's own preferences over them).</summary>
    public static TagSidebarChoice From(TagSettings tags) =>
        new(tags.SidebarCount, tags.RecordHistory, tags.SidebarPopular, tags.SidebarSearched, tags.SidebarLucky);
}
