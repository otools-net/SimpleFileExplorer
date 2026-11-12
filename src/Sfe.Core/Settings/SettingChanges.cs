using System.Text.Json.Nodes;

namespace Sfe.Core.Settings;

/// <summary>
/// Some settings of a level to change, the others left as they are (<see cref="SettingsService.SaveChangesAsync"/>).
/// </summary>
/// <param name="Values">New values by setting key (<c>player.seekSeconds</c>); a value equal to the inherited one
/// follows the levels above again.</param>
/// <param name="Lock">Settings this level locks for the levels below.</param>
/// <param name="Unlock">Settings this level stops locking.</param>
public sealed record SettingChanges(
    IReadOnlyDictionary<string, JsonNode?>? Values = null,
    IReadOnlyList<string>? Lock = null,
    IReadOnlyList<string>? Unlock = null
);
