namespace Sfe.Core.FileTracking;

/// <summary>
/// A video of a group and how much of it matches the group's reference (null for the reference itself).
/// </summary>
internal readonly record struct VideoMember(int Video, int? Percent);
