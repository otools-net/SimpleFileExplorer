namespace Sfe.Core.Options;

public sealed class TrashOptions
{
    /// <summary>
    /// Days a deleted file or folder stays in the trash before it is deleted for good; 0 = until emptied.
    /// </summary>
    public int RetentionDays { get; set; } = 30;
}
