using System.Text.Json.Nodes;
using Sfe.Core.Options;

namespace Sfe.Core.Settings;

/// <summary>The values of every level for one library and one user, as stored.</summary>
/// <param name="Environment">The configuration's values (and the code's defaults).</param>
/// <param name="EnvironmentLocks">Patterns of <see cref="SfeOptions.Locked"/>.</param>
public sealed record SettingSources(
    IReadOnlyDictionary<string, JsonNode?> Environment,
    IReadOnlyCollection<string> EnvironmentLocks,
    IReadOnlyDictionary<string, JsonNode?> Server,
    IReadOnlyCollection<string> ServerLocks,
    IReadOnlyDictionary<string, StoredSetting> Tenant,
    IReadOnlyDictionary<string, JsonNode?> User
);
