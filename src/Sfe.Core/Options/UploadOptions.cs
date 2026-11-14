namespace Otools.Net.Sfe.Core.Options;

public sealed class UploadOptions
{
    /// <summary>The largest file one may upload (megabytes); 0 = no limit (only the free space kept counts).</summary>
    public int MaxFileSizeMegabytes { get; set; }

    /// <summary>Hours an upload may stay unfinished without a chunk before what it received is removed.</summary>
    public int KeepUnfinishedHours { get; set; } = 48;
}
