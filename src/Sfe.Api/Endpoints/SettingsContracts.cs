namespace Otools.Net.Sfe.Api.Endpoints;

/// <param name="Keys">The settings this level locks for the levels below (its other lockable settings are
/// unlocked).</param>
public sealed record SettingLocks(IReadOnlyList<string>? Keys);
