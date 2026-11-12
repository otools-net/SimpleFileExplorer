namespace Sfe.Core.Options;

/// <summary>Duplicate files and files moved, renamed or deleted since they were indexed.</summary>
public sealed class FileTrackingOptions
{
    public DuplicateOptions Duplicates { get; set; } = new();
    public RelinkOptions Relink { get; set; } = new();
    public SimilarVideoOptions SimilarVideos { get; set; } = new();
}
