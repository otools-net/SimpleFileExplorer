using System.Text.Json.Serialization;

namespace Otools.Net.Sfe.Core.Options;

/// <summary>
/// When the web client plays a video through ffmpeg (<see cref="StreamingOptions"/>). Written as a name in the settings
/// file, like <see cref="Theme"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StreamingMode>))]
public enum StreamingMode
{
    /// <summary>Videos are only sent as they are.</summary>
    Off,

    /// <summary>A video the browser cannot play is converted by ffmpeg while it plays.</summary>
    Fallback,

    /// <summary>Every video is played through ffmpeg, even those the browser could play.</summary>
    Always,
}
