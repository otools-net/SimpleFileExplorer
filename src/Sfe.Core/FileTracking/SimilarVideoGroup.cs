namespace Otools.Net.Sfe.Core.FileTracking;

/// <param name="Count">Videos in the group; <paramref name="Videos"/> lists at most
/// <see cref="DuplicateFinder.MaxFilesPerGroup"/>.</param>
/// <param name="WastedBytes">Space taken by all videos but the biggest.</param>
/// <param name="Videos">The video the others are compared to (the longest, then the biggest) first.</param>
public sealed record SimilarVideoGroup(int Count, long WastedBytes, IReadOnlyList<SimilarVideo> Videos);
