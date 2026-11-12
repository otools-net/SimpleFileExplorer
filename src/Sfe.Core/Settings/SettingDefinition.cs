using Sfe.Core.Access;

namespace Sfe.Core.Settings;

/// <param name="Key">As the settings page names it, e.g. <c>player.seekSeconds</c> (also the key of its validation
/// errors).</param>
/// <param name="Scope">
/// The most specific level that may set it: <see cref="SettingLevel.Server"/> for what the server shares (its own
/// resources), <see cref="SettingLevel.Tenant"/> for what a library decides, <see cref="SettingLevel.User"/> for
/// personal preferences. Settings only the configuration sets (root, database, ffmpeg...) are not in the catalog.
/// </param>
/// <param name="Right">
/// The right a library's administrator needs to change it (server levels need <see cref="Rights.ServerSettings"/>);
/// <see cref="Rights.ServerSettings"/> itself for the settings about the server alone (how it starts).
/// </param>
/// <param name="PerLibrary">
/// Paths inside a library (relative to its root): no server-wide value, as they mean nothing in another library.
/// </param>
public sealed record SettingDefinition(string Key, SettingLevel Scope, string Right, bool PerLibrary);
