using System.Text.Json;
using System.Text.Json.Nodes;
using Sfe.Core.Access;

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

/// <param name="Key">As the settings page names it, e.g. <c>player.seekSeconds</c> (also the key of its validation errors).</param>
/// <param name="Scope">
/// The most specific level that may set it: <see cref="SettingLevel.Server"/> for what the server shares (its own
/// resources), <see cref="SettingLevel.Tenant"/> for what a library decides, <see cref="SettingLevel.User"/> for
/// personal preferences. Settings only the configuration sets (root, database, ffmpeg...) are not in the catalog.
/// </param>
/// <param name="Right">The right a library's administrator needs to change it (server levels need <see cref="Rights.ServerSettings"/>).</param>
/// <param name="PerLibrary">
/// Paths inside a library (relative to its root): no server-wide value, as they mean nothing in another library.
/// </param>
public sealed record SettingDefinition(string Key, SettingLevel Scope, string Right, bool PerLibrary);

/// <summary>Every setting of <see cref="EditableSettings"/>, with the level it can be set down to and its right.</summary>
public static class SettingCatalog
{
    private static readonly HashSet<string> ServerKeys =
    [
        "dataServer", "browse.maxPageSize", "search.maxPageSize", "scan.parallelism", "thumbnails.maxConcurrency",
        "thumbnails.timeoutSeconds", "trickplay.parallelism", "trickplay.timeoutMinutes", "tasks.logRetentionDays",
    ];

    private static readonly HashSet<string> UserKeys =
    [
        "appearance.theme", "browse.defaultPageSize", "search.defaultPageSize", "browse.folderPictures", "browse.showResolution",
        "browse.showDuration", "browse.resolutionMark", "thumbnails.singleViewOriginals", "player.seekSeconds", "player.loop",
        "trickplay.hoverPreview", "search.historySize", "tags.sidebarCount",
    ];

    private static readonly HashSet<string> PerLibraryKeys = ["scan.excludedFolders", "trickplay.folders", "editor.outputFolder"];

    /// <summary>The right of a key, by its first part (the settings page's sections).</summary>
    private static readonly (string Prefix, string Right)[] RightsByPrefix =
    [
        ("appearance.", Rights.InterfaceSettings), ("browse.", Rights.InterfaceSettings), ("search.", Rights.InterfaceSettings),
        ("player.", Rights.PlaybackSettings), ("dataServer", Rights.PlaybackSettings),
        ("showHiddenItems", Rights.ScanningSettings), ("scan.", Rights.ScanningSettings), ("fileTypes.", Rights.ScanningSettings),
        ("thumbnails.", Rights.ThumbnailSettings), ("trickplay.", Rights.TrickplaySettings), ("fileTracking.", Rights.TrackingSettings),
        ("tags.", Rights.TagSettings), ("tasks.", Rights.TaskSettings), ("editor.", Rights.EditorSettings),
    ];

    public static IReadOnlyList<SettingDefinition> All { get; } = SettingKeys.Flatten(SettingKeys.ToJson(new EditableSettings()))
        .Keys
        .Select(key => new SettingDefinition(
            key,
            ServerKeys.Contains(key) ? SettingLevel.Server : UserKeys.Contains(key) ? SettingLevel.User : SettingLevel.Tenant,
            RightsByPrefix.First(p => key.StartsWith(p.Prefix, StringComparison.Ordinal)).Right,
            PerLibraryKeys.Contains(key)))
        .ToList();

    private static readonly Dictionary<string, SettingDefinition> ByKey = All.ToDictionary(d => d.Key, StringComparer.Ordinal);

    public static SettingDefinition? Find(string key) => ByKey.GetValueOrDefault(key);

    /// <summary>
    /// <paramref name="pattern"/> names <paramref name="key"/>: equal, or a prefix ending with <c>*</c>
    /// (<c>player.*</c>; <c>*</c> alone names every setting).
    /// </summary>
    public static bool Matches(string pattern, string key) => RightCatalog.Matches(pattern, key);
}

/// <summary>Settings as JSON, flattened to their keys (<c>browse.defaultPageSize</c>) and back.</summary>
public static class SettingKeys
{
    public static JsonObject ToJson(EditableSettings settings) => JsonSerializer.SerializeToNode(settings, SettingsStore.Json)!.AsObject();

    public static EditableSettings FromJson(JsonObject json) => json.Deserialize<EditableSettings>(SettingsStore.Json)!;

    /// <summary>Objects open into dotted keys; anything else (numbers, text, lists) is a value.</summary>
    public static Dictionary<string, JsonNode?> Flatten(JsonObject json)
    {
        var values = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        Add(json, "");
        return values;

        void Add(JsonObject node, string prefix)
        {
            foreach (var (name, value) in node)
            {
                if (value is JsonObject child)
                    Add(child, prefix + name + ".");
                else
                    values[prefix + name] = value;
            }
        }
    }

    /// <summary>Sets the value of a dotted key, creating the objects on its way.</summary>
    public static void Set(JsonObject json, string key, JsonNode? value)
    {
        var parts = key.Split('.');
        var node = json;
        foreach (var part in parts[..^1])
        {
            if (node[part] is not JsonObject child)
                node[part] = child = new JsonObject();
            node = child;
        }

        node[parts[^1]] = value?.DeepClone();
    }

    public static bool Same(JsonNode? a, JsonNode? b) => JsonNode.DeepEquals(a, b);
}
