using System.Text.Json.Serialization;

namespace Sfe.Core.Options;

/// <summary>
/// Colors of the web client. Written as a name in the settings file too (it is serialized without the API's enum
/// converter), so that the file stays readable and editable by hand.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<Theme>))]
public enum Theme
{
    /// <summary>Follows the light or dark mode of the device.</summary>
    System,
    Dark,
    Light,
}
