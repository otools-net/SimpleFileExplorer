namespace Otools.Net.Sfe.Core.FileTracking;

/// <summary>Similar videos: the reference (the longest) first.</summary>
internal sealed record VideoGroup(IReadOnlyList<VideoMember> Members);
