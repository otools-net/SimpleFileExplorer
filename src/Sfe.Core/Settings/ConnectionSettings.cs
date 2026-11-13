using System.Text.RegularExpressions;
using Sfe.Core.Options;

namespace Sfe.Core.Settings;

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

    public static ConnectionSettings From(ConnectionOptions options) =>
        new()
        {
            Enabled = options.Enabled,
            Providers = [.. options.Providers],
            Levels = [.. options.Levels],
        };

    public void ApplyTo(ConnectionOptions options)
    {
        options.Enabled = Enabled;
        options.Providers = [.. Providers];
        options.Levels = [.. Levels];
    }

    /// <summary>Lower case, trimmed, each word once.</summary>
    public void Normalize()
    {
        Providers = Words(Providers);
        Levels = Words(Levels);
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
}
