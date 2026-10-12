using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Sfe.Core.Options;

namespace Sfe.Core.Settings;

/// <summary>
/// The settings file written by the Settings page. Its values are applied over the configuration
/// (<see cref="Apply"/> runs as a post-configuration of <see cref="SfeOptions"/>), and saving it signals
/// <see cref="IOptionsMonitor{TOptions}"/> so that services see the new values without a restart.
/// </summary>
/// <remarks>
/// Not a configuration provider: configuration merges lists item by item, so a shorter saved list would keep the
/// configuration's extra items. Here a saved list replaces the configured one, and keys missing from the file
/// (settings added by a later version) keep their configured value.
/// </remarks>
public sealed class SettingsStore : IOptionsChangeTokenSource<SfeOptions>
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly ILogger<SettingsStore> _logger;
    private readonly Lock _lock = new();
    private JsonObject? _saved;
    private CancellationTokenSource _changed = new();

    public SettingsStore(string path, ILogger<SettingsStore> logger)
    {
        FilePath = path;
        _logger = logger;
        _saved = Load();
    }

    public string FilePath { get; }

    /// <summary>A settings file is in use.</summary>
    public bool IsCustomized => _saved is not null;

    public string? Name => Microsoft.Extensions.Options.Options.DefaultName;

    public IChangeToken GetChangeToken() => new CancellationChangeToken(_changed.Token);

    internal void Apply(SfeOptions options)
    {
        var saved = _saved;
        if (saved is null)
            return;

        try
        {
            var merged = JsonSerializer.SerializeToNode(EditableSettings.From(options), Json)!.AsObject();
            Merge(merged, saved);
            merged.Deserialize<EditableSettings>(Json)!.ApplyTo(options);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Ignoring the settings in {File}: they don't match the expected format", FilePath);
        }
    }

    /// <summary>The settings the server's settings page locked for the libraries and users (keys, see <see cref="SettingCatalog"/>).</summary>
    public IReadOnlyList<string> Locked => _saved?[LockedKey] is JsonArray locked
        ? locked.Select(n => n?.GetValueKind() == JsonValueKind.String ? n.GetValue<string>() : null).OfType<string>().ToList()
        : [];

    /// <summary>The values saved, by key (without the locks).</summary>
    public IReadOnlyDictionary<string, JsonNode?> Values
    {
        get
        {
            if (_saved?.DeepClone() is not JsonObject saved)
                return new Dictionary<string, JsonNode?>();
            saved.Remove(LockedKey);
            return SettingKeys.Flatten(saved);
        }
    }

    /// <summary>Key of the locks in the file, next to the settings (versions before them ignore it).</summary>
    public const string LockedKey = "locked";

    /// <summary>Saves every setting as given (the server's settings without libraries and users).</summary>
    public Task SaveAsync(EditableSettings settings, CancellationToken cancellationToken = default) =>
        WriteAsync(JsonSerializer.SerializeToNode(settings, Json)!.AsObject(), cancellationToken);

    /// <summary>
    /// Saves the server level of the layered settings: the values that differ from the configuration, and the locks.
    /// Nothing to save removes the file, so that the configuration applies again.
    /// </summary>
    public async Task SaveLayerAsync(IReadOnlyDictionary<string, JsonNode?> values, IReadOnlyCollection<string> locked, CancellationToken cancellationToken = default)
    {
        if (values.Count == 0 && locked.Count == 0)
        {
            Reset();
            return;
        }

        var node = new JsonObject();
        foreach (var (key, value) in values.OrderBy(v => v.Key, StringComparer.Ordinal))
            SettingKeys.Set(node, key, value);
        if (locked.Count > 0)
            node[LockedKey] = new JsonArray(locked.Order(StringComparer.Ordinal).Select(k => (JsonNode?)JsonValue.Create(k)).ToArray());
        await WriteAsync(node, cancellationToken);
    }

    private async Task WriteAsync(JsonObject node, CancellationToken cancellationToken)
    {
        if (Path.GetDirectoryName(FilePath) is { Length: > 0 } directory)
            Directory.CreateDirectory(directory);

        // Written next to the target then moved, so a crash never leaves a half-written file.
        var temp = FilePath + ".tmp";
        await File.WriteAllTextAsync(temp, node.ToJsonString(Json), cancellationToken);
        File.Move(temp, FilePath, overwrite: true);

        lock (_lock)
            _saved = node;
        NotifyChanged();
    }

    /// <summary>Deletes the settings file: the configuration applies again.</summary>
    public void Reset()
    {
        if (!File.Exists(FilePath) && _saved is null)
            return;
        File.Delete(FilePath);
        lock (_lock)
            _saved = null;
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        // The monitor asks for a new token from its change callback: replace the source before cancelling it.
        var previous = Interlocked.Exchange(ref _changed, new CancellationTokenSource());
        previous.Cancel();
        previous.Dispose();
    }

    private JsonObject? Load()
    {
        if (!File.Exists(FilePath))
            return null;

        try
        {
            return JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogWarning(ex, "Cannot read the settings file {File}: the configuration applies", FilePath);
            return null;
        }
    }

    /// <summary>Copies <paramref name="source"/> over <paramref name="target"/>: objects merge, other values replace.</summary>
    private static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var (key, value) in source)
        {
            if (value is JsonObject child && target[key] is JsonObject existing)
                Merge(existing, child);
            else
                target[key] = value?.DeepClone();
        }
    }
}
