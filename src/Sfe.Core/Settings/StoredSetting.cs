using System.Text.Json.Nodes;

namespace Sfe.Core.Settings;

/// <param name="Locked">The library's users cannot change it for themselves.</param>
public sealed record StoredSetting(JsonNode? Value, bool Locked);
