namespace Sfe.Core.Access;

/// <summary>
/// The library a set of Core services works for (its settings, see <see cref="Settings.SettingLayers.ApplyTo"/>);
/// null = the first library, the configuration's.
/// </summary>
public sealed record TenantBinding(int? TenantId);
