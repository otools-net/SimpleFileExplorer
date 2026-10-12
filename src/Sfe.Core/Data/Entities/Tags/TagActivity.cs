namespace Sfe.Core.Data;

/// <summary>When a tag was last used, for the sidebar's "last searched" and "last viewed" lists (UTC).</summary>
public sealed class TagActivity
{
    public int TagId { get; set; }
    public Tag? Tag { get; set; }

    /// <summary>Last time a search named the tag.</summary>
    public DateTime? LastSearchedAt { get; set; }

    /// <summary>Last time a file with the tag was viewed.</summary>
    public DateTime? LastViewedAt { get; set; }
}
