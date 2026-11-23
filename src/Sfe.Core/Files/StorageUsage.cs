namespace Otools.Net.Sfe.Core.Files;

/// <summary>
/// The space of a storage: the library's drive or a connection's account. <paramref name="Reported"/> false when the
/// drive or the provider doesn't tell (the numbers then null); <paramref name="Total"/> and <paramref name="Free"/>
/// may be null on their own when a provider gives only what is used.
/// </summary>
public sealed record StorageUsage(bool Reported, long? Used, long? Total, long? Free)
{
    public static StorageUsage NotReported { get; } = new(false, null, null, null);

    /// <summary>A drive's space: what isn't free is used.</summary>
    public static StorageUsage OfDrive(long total, long free) => new(true, Math.Max(0, total - free), total, free);

    /// <summary>A provider's space: used, out of a total when it gives one.</summary>
    public static StorageUsage OfAccount(long used, long? total) =>
        new(true, used, total, total is { } all ? Math.Max(0, all - used) : null);
}
