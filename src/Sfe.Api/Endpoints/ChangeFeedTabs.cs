using System.Collections.Concurrent;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// The change feeds open now, by caller and tab (the feed's <c>tab</c> parameter, the <c>X-SFE-Tab</c> header of the
/// tab's requests), so that a tab can say which folder it shows (<c>PUT files/changes/watch</c>).
/// </summary>
internal static class ChangeFeedTabs
{
    private const int MaxTabLength = 64;

    private static readonly ConcurrentDictionary<string, ChangeFeedQueue> Open = new(StringComparer.Ordinal);

    /// <summary>A tab's name as the client makes them (letters, digits, <c>-</c>, <c>_</c>); others are not kept.</summary>
    public static bool IsValid(string? tab) =>
        !string.IsNullOrEmpty(tab)
        && tab.Length <= MaxTabLength
        && tab.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>Keeps <paramref name="feed"/> as the tab's (a newer feed of the same tab replaces an older one).</summary>
    public static void Add(string caller, string tab, ChangeFeedQueue feed) => Open[Key(caller, tab)] = feed;

    /// <summary>Forgets <paramref name="feed"/>, unless a newer feed of the tab replaced it already.</summary>
    public static void Remove(string caller, string tab, ChangeFeedQueue feed) =>
        Open.TryRemove(new KeyValuePair<string, ChangeFeedQueue>(Key(caller, tab), feed));

    public static ChangeFeedQueue? Find(string caller, string? tab) =>
        IsValid(tab) ? Open.GetValueOrDefault(Key(caller, tab!)) : null;

    private static string Key(string caller, string tab) => caller + "\n" + tab;
}
