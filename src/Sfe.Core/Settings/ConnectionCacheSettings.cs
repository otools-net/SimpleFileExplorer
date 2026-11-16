using Otools.Net.Sfe.Core.Options;

namespace Otools.Net.Sfe.Core.Settings;

/// <summary>The editable part of <see cref="ConnectionCacheOptions"/>: its limits (server settings).</summary>
public sealed class ConnectionCacheSettings
{
    /// <summary>Above this the cache could fill a disk unseen: a terabyte is the most.</summary>
    public const int MaxSizeLimit = 1024 * 1024;

    /// <inheritdoc cref="ConnectionCacheOptions.MaxSizeMegabytes"/>
    public int MaxSizeMegabytes { get; set; }

    /// <inheritdoc cref="ConnectionCacheOptions.MaxFileMegabytes"/>
    public int MaxFileMegabytes { get; set; }

    /// <inheritdoc cref="ConnectionCacheOptions.MaxAgeDays"/>
    public int MaxAgeDays { get; set; }

    public static ConnectionCacheSettings From(ConnectionCacheOptions options) =>
        new()
        {
            MaxSizeMegabytes = options.MaxSizeMegabytes,
            MaxFileMegabytes = options.MaxFileMegabytes,
            MaxAgeDays = options.MaxAgeDays,
        };

    public void ApplyTo(ConnectionCacheOptions options)
    {
        options.MaxSizeMegabytes = MaxSizeMegabytes;
        options.MaxFileMegabytes = MaxFileMegabytes;
        options.MaxAgeDays = MaxAgeDays;
    }

    /// <summary>What is wrong with the values, by setting key; nothing when they are fine.</summary>
    public IEnumerable<(string Key, string Message)> Errors()
    {
        if (MaxSizeMegabytes is < 0 or > MaxSizeLimit)
            yield return ("connections.cache.maxSizeMegabytes", $"From 0 (off) to {MaxSizeLimit} MB.");
        if (MaxFileMegabytes is < 1 or > MaxSizeLimit)
            yield return ("connections.cache.maxFileMegabytes", $"From 1 to {MaxSizeLimit} MB.");
        if (MaxAgeDays is < 1 or > 3650)
            yield return ("connections.cache.maxAgeDays", "From 1 to 3650 days.");
    }
}
