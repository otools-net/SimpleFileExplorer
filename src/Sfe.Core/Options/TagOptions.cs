namespace Otools.Net.Sfe.Core.Options;

public sealed class TagOptions
{
    /// <summary>
    /// Record the tags that are searched for and the tags of viewed files (the sidebar's history lists).
    /// </summary>
    public bool RecordHistory { get; set; } = true;

    /// <summary>Tags shown in each section of the client's sidebar.</summary>
    public int SidebarCount { get; set; } = 5;

    /// <summary>The sidebar shows the "Most popular" tags (off by default since 2.0: little used).</summary>
    public bool SidebarPopular { get; set; }

    /// <summary>The sidebar shows the "Last searched" tags (while <see cref="RecordHistory"/> is on).</summary>
    public bool SidebarSearched { get; set; }

    /// <summary>The sidebar shows "I feel lucky": random tags in none of its other lists.</summary>
    public bool SidebarLucky { get; set; }
}
