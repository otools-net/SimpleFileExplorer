namespace Sfe.Core.Data;

/// <summary>A preference of one user, over the defaults of the library (<see cref="Settings.SettingCatalog"/>).</summary>
public sealed class UserSetting
{
    public int UserId { get; set; }
    public string Key { get; set; } = string.Empty;

    /// <summary>The value as JSON.</summary>
    public string Value { get; set; } = string.Empty;
}
