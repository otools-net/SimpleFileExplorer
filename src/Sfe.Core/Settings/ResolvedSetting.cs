using System.Text.Json.Nodes;

namespace Otools.Net.Sfe.Core.Settings;

/// <param name="Value">In effect at the level asked for.</param>
/// <param name="Source">The level it comes from.</param>
/// <param name="LockedAt">The level that locked it (the levels below cannot change it); null = not locked.</param>
/// <param name="Inherited">What the level asked for gets from the levels above, without a value of its own.</param>
public sealed record ResolvedSetting(JsonNode? Value, SettingLevel Source, SettingLevel? LockedAt, JsonNode? Inherited);
