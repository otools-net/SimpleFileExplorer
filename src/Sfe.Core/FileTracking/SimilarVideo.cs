using Sfe.Core.Library;

namespace Sfe.Core.FileTracking;

/// <param name="Match">Matching frames with the first video of the group, in percent of the longer one; null for that
/// first video.</param>
/// <param name="Duration">Seconds, as seen by the trickplay (frames × interval).</param>
public sealed record SimilarVideo(LibraryItem File, int? Match, int Duration);
