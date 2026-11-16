using System.Text.RegularExpressions;
using Otools.Net.Sfe.Core.Options;

namespace Otools.Net.Sfe.Core.Settings;

/// <summary>
/// The editable part of <see cref="ConnectionOptions"/>: server settings, for its administrators.
/// <see cref="ConnectionOptions.AllowPrivateAddresses"/> stays in the configuration.
/// </summary>
public sealed partial class ConnectionSettings
{
    private static readonly string[] KnownLevels =
    [
        ConnectionOptions.AccountLevel,
        ConnectionOptions.LibraryLevel,
        ConnectionOptions.ServerLevel,
    ];

    /// <inheritdoc cref="ConnectionOptions.Enabled"/>
    public bool Enabled { get; set; }

    /// <inheritdoc cref="ConnectionOptions.Providers"/>
    public List<string> Providers { get; set; } = [];

    /// <inheritdoc cref="ConnectionOptions.Levels"/>
    public List<string> Levels { get; set; } = [];

    public DropboxSettings Dropbox { get; set; } = new();

    public ConnectionCacheSettings Cache { get; set; } = new();

    public static ConnectionSettings From(ConnectionOptions options) =>
        new()
        {
            Enabled = options.Enabled,
            Providers = [.. options.Providers],
            Levels = [.. options.Levels],
            Dropbox = new() { AppKey = options.Dropbox.AppKey },
            Cache = ConnectionCacheSettings.From(options.Cache),
        };

    public void ApplyTo(ConnectionOptions options)
    {
        options.Enabled = Enabled;
        options.Providers = [.. Providers];
        options.Levels = [.. Levels];
        options.Dropbox.AppKey = Dropbox.AppKey;
        Cache.ApplyTo(options.Cache);
    }

    /// <summary>Lower case, trimmed, each word once.</summary>
    public void Normalize()
    {
        Providers = Words(Providers);
        Levels = Words(Levels);
        Dropbox.AppKey = string.IsNullOrWhiteSpace(Dropbox.AppKey) ? null : Dropbox.AppKey.Trim();
    }

    /// <summary>What is wrong with the values, by setting key; nothing when they are fine.</summary>
    public IEnumerable<(string Key, string Message)> Errors()
    {
        if (Providers.Any(kind => !KindName().IsMatch(kind)))
        {
            yield return (
                "connections.providers",
                "A kind of connection is a short word of lowercase letters, digits and '-' (dropbox)."
            );
        }
        if (Levels.Except(KnownLevels).Any())
            yield return ("connections.levels", $"The levels are: {string.Join(", ", KnownLevels)}.");
        if (Dropbox.AppKey is { } key && !AppKey().IsMatch(key))
            yield return ("connections.dropbox.appKey", "An app key is letters and digits, as Dropbox shows it.");
        foreach (var error in Cache.Errors())
            yield return error;
    }

    private static List<string> Words(IEnumerable<string>? words) =>
        [
            .. (words ?? [])
                .Select(word => word?.Trim().ToLowerInvariant() ?? string.Empty)
                .Where(word => word.Length > 0)
                .Distinct(StringComparer.Ordinal),
        ];

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,31}$")]
    private static partial Regex KindName();

    [GeneratedRegex("^[A-Za-z0-9]{1,64}$")]
    private static partial Regex AppKey();
}
