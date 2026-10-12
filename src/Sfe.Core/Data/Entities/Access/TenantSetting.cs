namespace Sfe.Core.Data;

/// <summary>
/// A setting of one library, over the server's (<see cref="Settings.SettingCatalog"/>). One row per setting, so a
/// setting added later needs no new column.
/// </summary>
public sealed class TenantSetting
{
    public int TenantId { get; set; }

    /// <summary>The setting, as the settings page names it (<c>player.seekSeconds</c>).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>The value as JSON.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Users of the library cannot change it for themselves.</summary>
    public bool Locked { get; set; }
}
