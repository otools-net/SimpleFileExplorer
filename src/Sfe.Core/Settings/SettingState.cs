using System.Text.Json.Nodes;

namespace Otools.Net.Sfe.Core.Settings;

/// <summary>How a setting shows on the settings page of a level.</summary>
/// <param name="Scope">The most specific level that may set it (<see cref="SettingDefinition.Scope"/>).</param>
/// <param name="Source">Where its value comes from.</param>
/// <param name="Editable">This level may change it: within its scope, not locked by a level above, and (for a library's
/// administrator) with the right.</param>
/// <param name="Lockable">This level may lock it for the levels below (only when one of them could change it).</param>
/// <param name="Locked">This level locked it.</param>
/// <param name="LockedBy">The level above that locked it, when one did.</param>
/// <param name="Inherited">The value of the levels above, which applies without one of this level.</param>
public sealed record SettingState(
    SettingLevel Scope,
    SettingLevel Source,
    bool Editable,
    bool Lockable,
    bool Locked,
    SettingLevel? LockedBy,
    JsonNode? Inherited
);
