namespace Otools.Net.Sfe.Core.Options;

/// <summary>
/// Duplicates are indexed files of the same size and fingerprint (<see cref="ScanOptions.ComputeHashes"/>): files
/// indexed without a fingerprint are never listed.
/// </summary>
public sealed class DuplicateOptions
{
    /// <summary>
    /// Smaller files are left out: a library is full of identical tiny files (empty files, sidecars).
    /// </summary>
    public int MinimumKilobytes { get; set; } = 1;

    /// <summary>Groups of identical files loaded at a time.</summary>
    public int GroupsPerPage { get; set; } = 50;

    /// <summary>
    /// Offer to delete a copy from the disk, its tags going to the copy kept. The two files are compared byte for
    /// byte first, so the content is never lost.
    /// </summary>
    public bool AllowRemove { get; set; } = true;
}
