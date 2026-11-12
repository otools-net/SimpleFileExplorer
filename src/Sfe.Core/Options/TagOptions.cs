namespace Sfe.Core.Options;

public sealed class TagOptions
{
    /// <summary>
    /// Record the tags that are searched for and the tags of viewed files (the sidebar's history lists).
    /// </summary>
    public bool RecordHistory { get; set; } = true;

    /// <summary>Tags shown in each section of the client's sidebar.</summary>
    public int SidebarCount { get; set; } = 5;
}
