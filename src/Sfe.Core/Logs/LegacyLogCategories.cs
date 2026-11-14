using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Otools.Net.Sfe.Core.Logs;

/// <summary>
/// Keeps the log levels written for the categories SFE had before its namespaces became <c>Otools.Net.Sfe.*</c>: a
/// rule of <c>Logging:LogLevel</c> (or of a provider's section, <c>Logging:Console:LogLevel</c>…) naming <c>Sfe…</c>
/// also applies to <c>Otools.Net.Sfe…</c>, unless a rule names that category already. Runs each time the options are
/// built, so edits of the settings files are followed.
/// </summary>
public sealed class LegacyLogCategories : IPostConfigureOptions<LoggerFilterOptions>
{
    public const string OldPrefix = "Sfe";
    public const string NewPrefix = "Otools.Net.";

    public void PostConfigure(string? name, LoggerFilterOptions options)
    {
        var rules = options.Rules;
        var copies = rules
            .Where(rule => rule.CategoryName?.StartsWith(OldPrefix, StringComparison.OrdinalIgnoreCase) == true)
            .Select(rule => new LoggerFilterRule(
                rule.ProviderName,
                NewPrefix + rule.CategoryName,
                rule.LogLevel,
                rule.Filter
            ))
            .Where(copy => !rules.Any(rule => SameTarget(rule, copy)))
            .ToList();
        foreach (var copy in copies)
            rules.Add(copy);
    }

    private static bool SameTarget(LoggerFilterRule a, LoggerFilterRule b) =>
        string.Equals(a.ProviderName, b.ProviderName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.CategoryName, b.CategoryName, StringComparison.OrdinalIgnoreCase);
}
