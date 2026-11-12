namespace Sfe.Core.Settings;

/// <summary>
/// Where a setting's value comes from, from the most general to the most specific: the configuration (files,
/// environment variables), the server's settings page (<c>settings.json</c>), a library, a user. A level's value wins
/// over those above unless one of them locked the setting.
/// </summary>
public enum SettingLevel
{
    Environment,
    Server,
    Tenant,
    User,
}
