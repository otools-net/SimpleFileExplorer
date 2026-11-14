namespace Otools.Net.Sfe.Core.Trickplay;

public enum TrickplayState
{
    /// <summary>Turned off in the settings.</summary>
    Disabled,

    /// <summary>Turned on, but ffmpeg can't be found.</summary>
    Unavailable,
    Idle,
    Running,
}
