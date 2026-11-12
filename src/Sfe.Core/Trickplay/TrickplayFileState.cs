namespace Sfe.Core.Trickplay;

/// <summary>Where a video stands in the background job; videos with nothing to do have no state.</summary>
public enum TrickplayFileState
{
    /// <summary>Waiting for its turn (opened in the player, or found by a pass).</summary>
    Pending,

    /// <summary>ffmpeg is working on it.</summary>
    Running,
}
