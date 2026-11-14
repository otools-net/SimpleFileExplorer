using Otools.Net.Sfe.Core.Options;

namespace Otools.Net.Sfe.Core.Settings;

/// <summary>The editable <see cref="AccessTokenOptions"/>: server settings, for its administrators.</summary>
public sealed class AccessTokenSettings
{
    /// <inheritdoc cref="AccessTokenOptions.Enabled"/>
    public bool Enabled { get; set; }

    /// <inheritdoc cref="AccessTokenOptions.MaxLifetimeDays"/>
    public int MaxLifetimeDays { get; set; }

    /// <inheritdoc cref="AccessTokenOptions.MaxPerAccount"/>
    public int MaxPerAccount { get; set; }

    public static AccessTokenSettings From(AccessTokenOptions options) =>
        new()
        {
            Enabled = options.Enabled,
            MaxLifetimeDays = options.MaxLifetimeDays,
            MaxPerAccount = options.MaxPerAccount,
        };

    public void ApplyTo(AccessTokenOptions options)
    {
        options.Enabled = Enabled;
        options.MaxLifetimeDays = MaxLifetimeDays;
        options.MaxPerAccount = MaxPerAccount;
    }

    /// <summary>What is wrong with the values, by setting key; nothing when they are fine.</summary>
    public IEnumerable<(string Key, string Message)> Errors()
    {
        if (MaxLifetimeDays is < 0 or > AccessTokenOptions.MaxLifetimeDaysLimit)
        {
            yield return (
                "security.accessTokens.maxLifetimeDays",
                $"The longest life of a token is 0 (no limit) to {AccessTokenOptions.MaxLifetimeDaysLimit} days."
            );
        }
        if (MaxPerAccount is < 1 or > AccessTokenOptions.MaxPerAccountLimit)
        {
            yield return (
                "security.accessTokens.maxPerAccount",
                $"The tokens of an account are 1 to {AccessTokenOptions.MaxPerAccountLimit}."
            );
        }
    }
}
