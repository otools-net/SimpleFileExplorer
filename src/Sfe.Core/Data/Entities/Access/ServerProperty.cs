namespace Sfe.Core.Data;

/// <summary>
/// A fact about the install as a whole, by key: <see cref="VersionKey"/> is the version of SFE that last set up or
/// updated the databases and settings, which tells a newer version that it has to update them first.
/// </summary>
public sealed class ServerProperty
{
    public const string VersionKey = "Version";

    /// <summary>When <see cref="VersionKey"/> was last written (round-trip UTC).</summary>
    public const string UpdatedKey = "VersionUpdated";

    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
