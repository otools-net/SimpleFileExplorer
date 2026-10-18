using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Sfe.Core.Access;
using Sfe.Core.Library;
using Sfe.Core.Options;
using Sfe.Core.Paths;

namespace Sfe.Core.Settings;

/// <summary>Some settings of a level to change, the others left as they are (<see cref="SettingsService.SaveChangesAsync"/>).</summary>
/// <param name="Values">New values by setting key (<c>player.seekSeconds</c>); a value equal to the inherited one follows the levels above again.</param>
/// <param name="Lock">Settings this level locks for the levels below.</param>
/// <param name="Unlock">Settings this level stops locking.</param>
public sealed record SettingChanges(IReadOnlyDictionary<string, JsonNode?>? Values = null, IReadOnlyList<string>? Lock = null, IReadOnlyList<string>? Unlock = null);

/// <summary>
/// Reads and changes the settings exposed to the web client (see <see cref="EditableSettings"/>), by level
/// (<see cref="SettingLevel"/>). Without the accounts' database (<paramref name="layers"/> null: the Core alone, in
/// tests) there is only the server level, saved whole as before levels existed.
/// </summary>
public sealed class SettingsService(IOptionsMonitor<SfeOptions> options, SettingsStore store, SettingLayers? layers = null, TenantBinding? binding = null)
{
    public const int MaxPageSize = 10_000;

    /// <summary>Most searches a browser remembers (<c>Search:HistorySize</c>).</summary>
    public const int MaxSearchHistory = 100;

    /// <summary>Largest high resolution threshold, in pixels (a 16K picture is 15360×8640).</summary>
    public const int MaxHighResolutionPixels = 20000;

    /// <summary>1 GB: no photo is that large, so a bigger value is a typo.</summary>
    public const int MaxLargeImageKilobytes = 1_048_576;

    /// <summary>Ten years: longer is the same as keeping them until found (0).</summary>
    public const int MaxRetentionDays = 3650;

    /// <summary>Room for an Android intent link with a few extras.</summary>
    public const int MaxExternalUrlLength = 1000;

    /// <summary>A week: an external player's link is meant for watching now, not for sharing.</summary>
    public const int MaxLinkLifetimeMinutes = 10_080;

    /// <summary>Leaves room under SQL Server's 260 characters for the originals' folders and names.</summary>
    public const int MaxEditorFolderLength = 100;
    /// <summary>Longest name of a font of the reader's device (<c>reading.customFont</c>).</summary>
    public const int MaxCustomFontLength = 100;

    /// <summary>Videos converted at once (<c>Streaming:MaxStreams</c>).</summary>
    public const int MaxStreams = 32;

    /// <summary>Range of x264's constant rate factor (<c>Streaming:Quality</c>): beyond it, files grow for nothing or look bad.</summary>
    public const int MinStreamQuality = 15, MaxStreamQuality = 40;

    /// <summary>Range of <c>Streaming:MaxHeight</c> (besides 0, the video's own).</summary>
    public const int MinStreamHeight = 144, MaxStreamHeight = 4320;

    /// <summary>The server level: the configuration with the server settings page's values over it.</summary>
    public SettingsView Get()
    {
        if (layers is null)
            return new(EditableSettings.From(options.CurrentValue), store.IsCustomized);

        var sources = new SettingSources(Environment(out var locks), locks, store.Values, store.Locked, new Dictionary<string, StoredSetting>(), new Dictionary<string, JsonNode?>());
        return View(SettingLevel.Server, sources, null);
    }

    /// <summary>The settings of a level as its page shows them: values in effect, where they come from, what may change.</summary>
    /// <param name="tenantId">The library (tenant and user levels); null = the first library.</param>
    /// <param name="userId">The user (user level).</param>
    /// <param name="canChange">Whether the person may change a setting (their rights); null = every one.</param>
    public async Task<SettingsView> GetAsync(SettingLevel level, int? tenantId, int? userId, Func<SettingDefinition, bool>? canChange = null,
        CancellationToken cancellationToken = default)
    {
        if (layers is null || level == SettingLevel.Server)
            return Get() with { Keys = layers is null ? null : SettingLayers.States(SettingLayers.Resolve(ServerSources(), SettingLevel.Server), SettingLevel.Server, canChange) };

        var sources = await layers.LoadAsync(Tenant(tenantId), userId, cancellationToken);
        return View(level, sources, canChange);
    }

    /// <summary>The settings a user's web client works with: the library's, with the user's preferences over them.</summary>
    public async Task<EditableSettings> GetEffectiveAsync(int? tenantId, int? userId, CancellationToken cancellationToken = default)
    {
        if (layers is null)
            return EditableSettings.From(options.CurrentValue);

        var sources = await layers.LoadAsync(Tenant(tenantId), userId, cancellationToken);
        return SettingLayers.ToSettings(SettingLayers.Resolve(sources, SettingLevel.User));
    }

    /// <summary>Normalizes and validates <paramref name="settings"/>, then saves them at the server level; they apply right away.</summary>
    /// <returns>The settings in effect, or the validation errors keyed by setting (e.g. <c>browse.maxPageSize</c>).</returns>
    public async Task<(SettingsView? View, Dictionary<string, string[]>? Errors)> SaveAsync(EditableSettings settings, CancellationToken cancellationToken = default)
    {
        if (layers is not null)
            return await SaveAsync(SettingLevel.Server, null, null, settings, null, cancellationToken);

        Normalize(settings);
        var errors = Validate(settings);
        if (errors.Count > 0)
            return (null, errors);

        await store.SaveAsync(settings, cancellationToken);
        return (Get(), null);
    }

    /// <summary>
    /// Saves the settings of a level: those it may change and that differ from what it inherits (the rest follows
    /// the levels above). A change to a setting it may not change (locked above, outside its scope, no right) is an
    /// error, keyed by the setting.
    /// </summary>
    public async Task<(SettingsView? View, Dictionary<string, string[]>? Errors)> SaveAsync(
        SettingLevel level, int? tenantId, int? userId, EditableSettings settings, Func<SettingDefinition, bool>? canChange,
        CancellationToken cancellationToken = default)
    {
        Normalize(settings);
        var errors = Validate(settings);
        if (errors.Count > 0)
            return (null, errors);
        if (layers is null)
            throw new InvalidOperationException("Settings by level need the accounts' database.");

        var sources = level == SettingLevel.Server ? ServerSources() : await layers.LoadAsync(Tenant(tenantId), userId, cancellationToken);
        var resolved = SettingLayers.Resolve(sources, level);
        var states = SettingLayers.States(resolved, level, canChange);
        var submitted = SettingKeys.Flatten(SettingKeys.ToJson(settings));
        // Compared as saved: the configuration may say "" where a save says null.
        var current = Normalized(resolved, s => s.Value);
        var inherited = Normalized(resolved, s => s.Inherited);
        var kept = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var definition in SettingCatalog.All)
        {
            var key = definition.Key;
            var state = states[key];
            var value = submitted.GetValueOrDefault(key);
            if (!state.Editable)
            {
                if (!SettingKeys.Same(value, current[key]))
                    errors[key] = [Unchangeable(definition, state, level)];
                continue;
            }

            if (!SettingKeys.Same(value, inherited[key]) || state.Locked)
                kept[key] = value;
        }

        // Settings the person may not change keep the value this level gave them.
        foreach (var (key, state) in states.Where(s => !s.Value.Editable && resolved[s.Key].Source == level && s.Value.LockedBy is null))
            kept[key] = resolved[key].Value;

        if (errors.Count > 0)
            return (null, errors);

        await WriteAsync(level, tenantId, userId, kept, sources, cancellationToken);
        return (await GetAsync(level, tenantId, userId, canChange, cancellationToken), null);
    }

    /// <summary>
    /// Saves only the settings named in <paramref name="changes"/> at a level; what the level stores for the others
    /// stays as it is. Unlike <see cref="SaveAsync(SettingLevel, int?, int?, EditableSettings, Func{SettingDefinition, bool}?, CancellationToken)"/>,
    /// which takes every value, a page holding values loaded before another level changed cannot pin them here: an
    /// inherited value nobody touched keeps following the levels above. The values are checked together with those
    /// in effect (a page size against the largest page), and nothing is written when any check fails.
    /// </summary>
    public async Task<(SettingsView? View, Dictionary<string, string[]>? Errors)> SaveChangesAsync(
        SettingLevel level, int? tenantId, int? userId, SettingChanges changes, Func<SettingDefinition, bool>? canChange,
        CancellationToken cancellationToken = default)
    {
        if (layers is null || level is not (SettingLevel.Server or SettingLevel.Tenant or SettingLevel.User))
            throw new InvalidOperationException("Settings by level need the accounts' database.");

        var sources = level == SettingLevel.Server ? ServerSources() : await layers.LoadAsync(Tenant(tenantId), userId, cancellationToken);
        var resolved = SettingLayers.Resolve(sources, level);
        var states = SettingLayers.States(resolved, level, canChange);
        var submitted = changes.Values ?? new Dictionary<string, JsonNode?>();
        var toLock = (changes.Lock ?? []).ToHashSet(StringComparer.Ordinal);
        var toUnlock = (changes.Unlock ?? []).ToHashSet(StringComparer.Ordinal);
        var errors = new Dictionary<string, string[]>();

        foreach (var key in submitted.Keys.Where(k => SettingCatalog.Find(k) is null))
            errors[key] = ["There is no such setting."];
        foreach (var key in toLock.Concat(toUnlock).Where(k => level == SettingLevel.User || !states.TryGetValue(k, out var state) || !state.Lockable))
            errors[key] = ["This setting cannot be locked here."];
        foreach (var key in toLock.Intersect(toUnlock))
            errors[key] = ["A setting cannot be locked and unlocked at once."];
        var known = submitted.Where(s => SettingCatalog.Find(s.Key) is not null).ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);

        // The values in effect with the changes over them, checked and normalized together.
        var candidate = new JsonObject();
        foreach (var (key, setting) in resolved)
            SettingKeys.Set(candidate, key, setting.Value);
        foreach (var (key, value) in known)
            SettingKeys.Set(candidate, key, value);
        EditableSettings settings;
        try
        {
            settings = SettingKeys.FromJson(candidate);
        }
        catch (JsonException)
        {
            foreach (var key in known.Keys.Where(k => !Readable(resolved, k, known[k])))
                errors[key] = ["This value has the wrong type for this setting."];
            return (null, errors);
        }

        Normalize(settings);
        foreach (var (key, messages) in Validate(settings))
            errors.TryAdd(key, messages);
        var normalized = SettingKeys.Flatten(SettingKeys.ToJson(settings));
        var current = Normalized(resolved, s => s.Value);
        var inherited = Normalized(resolved, s => s.Inherited);

        var locked = states.Where(s => s.Value.Locked).Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        locked.UnionWith(toLock);
        locked.ExceptWith(toUnlock);

        // The level's own values, as stored, with each change written over (or removed when it is the inherited one).
        var layer = level switch
        {
            SettingLevel.Server => sources.Server.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal),
            SettingLevel.Tenant => sources.Tenant.ToDictionary(v => v.Key, v => v.Value.Value, StringComparer.Ordinal),
            _ => sources.User.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal),
        };
        foreach (var key in known.Keys)
        {
            var value = normalized[key];
            var state = states[key];
            if (!state.Editable)
            {
                if (!SettingKeys.Same(value, current[key]))
                    errors.TryAdd(key, [Unchangeable(SettingCatalog.Find(key)!, state, level)]);
                continue;
            }

            if (SettingKeys.Same(value, inherited[key]) && !locked.Contains(key))
                layer.Remove(key);
            else
                layer[key] = value;
        }

        if (errors.Count > 0)
            return (null, errors);

        switch (level)
        {
            case SettingLevel.Server:
                await store.SaveLayerAsync(layer, toLock.Count + toUnlock.Count > 0 ? locked : store.Locked, cancellationToken);
                layers.NotifyChanged();
                break;
            case SettingLevel.Tenant:
                var tenant = Tenant(tenantId) ?? throw new InvalidOperationException("No library yet.");
                // A newly locked setting pins the value in effect; an unlocked one with the inherited value needs no row.
                foreach (var key in toLock.Where(k => !layer.ContainsKey(k)))
                    layer[key] = normalized[key];
                var rows = layer.ToDictionary(v => v.Key, v => new StoredSetting(v.Value, locked.Contains(v.Key)), StringComparer.Ordinal);
                foreach (var key in toUnlock.Where(k => rows.ContainsKey(k) && SettingKeys.Same(rows[k].Value, inherited[k])))
                    rows.Remove(key);
                await layers.Rows.SaveTenantAsync(tenant, rows, cancellationToken);
                break;
            default:
                await layers.Rows.SaveUserAsync(userId ?? throw new InvalidOperationException("No user."), layer, cancellationToken);
                break;
        }

        return (await GetAsync(level, tenantId, userId, canChange, cancellationToken), null);
    }

    /// <summary>The settings in effect still read with <paramref name="value"/> for <paramref name="key"/> alone (its type fits).</summary>
    private static bool Readable(Dictionary<string, ResolvedSetting> resolved, string key, JsonNode? value)
    {
        var json = new JsonObject();
        foreach (var (k, setting) in resolved)
            SettingKeys.Set(json, k, setting.Value);
        SettingKeys.Set(json, key, value);
        try
        {
            SettingKeys.FromJson(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Locks <paramref name="keys"/> at a level (the levels below cannot change them) and unlocks the others it may
    /// lock. A newly locked setting keeps the value in effect.
    /// </summary>
    public async Task<(SettingsView? View, Dictionary<string, string[]>? Errors)> SaveLocksAsync(
        SettingLevel level, int? tenantId, IReadOnlyCollection<string> keys, Func<SettingDefinition, bool>? canChange,
        CancellationToken cancellationToken = default)
    {
        if (layers is null || level is not (SettingLevel.Server or SettingLevel.Tenant))
            throw new InvalidOperationException("Only the server and library levels lock settings.");

        var sources = level == SettingLevel.Server ? ServerSources() : await layers.LoadAsync(Tenant(tenantId), null, cancellationToken);
        var resolved = SettingLayers.Resolve(sources, level);
        var states = SettingLayers.States(resolved, level, canChange);
        var wanted = keys.ToHashSet(StringComparer.Ordinal);
        var errors = new Dictionary<string, string[]>();
        foreach (var key in wanted.Where(k => !states.TryGetValue(k, out var state) || !state.Lockable && !state.Locked))
            errors[key] = ["This setting cannot be locked here."];
        if (errors.Count > 0)
            return (null, errors);

        // Locks the person may not change stay as they are.
        var locked = states.Where(s => s.Value.Lockable ? wanted.Contains(s.Key) : s.Value.Locked).Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        if (level == SettingLevel.Server)
        {
            await store.SaveLayerAsync(store.Values, locked, cancellationToken);
            layers.NotifyChanged();
        }
        else
        {
            var tenant = Tenant(tenantId) ?? throw new InvalidOperationException("No library yet.");
            var rows = sources.Tenant.ToDictionary(r => r.Key, r => r.Value with { Locked = locked.Contains(r.Key) }, StringComparer.Ordinal);
            foreach (var key in locked.Where(k => !rows.ContainsKey(k)))
                rows[key] = new StoredSetting(resolved[key].Value, true);
            // An unlocked setting with the inherited value needs no row any more.
            foreach (var key in rows.Where(r => !r.Value.Locked && SettingKeys.Same(r.Value.Value, resolved[r.Key].Inherited)).Select(r => r.Key).ToList())
                rows.Remove(key);
            await layers.Rows.SaveTenantAsync(tenant, rows, cancellationToken);
        }

        return (await GetAsync(level, tenantId, null, canChange, cancellationToken), null);
    }

    /// <summary>Adds a library folder to <see cref="ScanOptions.ExcludedFolders"/> (saved like the other settings, for the library).</summary>
    public async Task ExcludeFolderAsync(string relativePath, int? tenantId = null, CancellationToken cancellationToken = default)
    {
        if (layers is null)
        {
            var settings = EditableSettings.From(options.CurrentValue);
            if (settings.Scan.ExcludedFolders.Contains(relativePath, LibraryPaths.PathComparer))
                return;
            settings.Scan.ExcludedFolders.Add(relativePath);
            Normalize(settings);
            await store.SaveAsync(settings, cancellationToken);
            return;
        }

        var sources = await layers.LoadAsync(Tenant(tenantId), null, cancellationToken);
        var resolved = SettingLayers.Resolve(sources, SettingLevel.Tenant);
        var current = SettingLayers.ToSettings(resolved);
        if (current.Scan.ExcludedFolders.Contains(relativePath, LibraryPaths.PathComparer))
            return;

        current.Scan.ExcludedFolders.Add(relativePath);
        Normalize(current);
        var tenant = Tenant(tenantId) ?? throw new InvalidOperationException("No library yet.");
        var rows = sources.Tenant.ToDictionary(r => r.Key, r => r.Value, StringComparer.Ordinal);
        const string key = "scan.excludedFolders";
        rows[key] = new StoredSetting(SettingKeys.Flatten(SettingKeys.ToJson(current))[key], rows.GetValueOrDefault(key)?.Locked ?? false);
        await layers.Rows.SaveTenantAsync(tenant, rows, cancellationToken);
    }

    /// <summary>
    /// The library's settings naming folders (<see cref="SettingCatalog"/>'s per-library keys: excluded folders,
    /// trickplay folders, the editor's folder) follow the folder <paramref name="from"/>, renamed or moved to
    /// <paramref name="to"/> (root-relative, canonical), with what is below it. Saved for the library, like
    /// <see cref="ExcludeFolderAsync"/>.
    /// </summary>
    /// <returns>The keys changed.</returns>
    public async Task<IReadOnlyList<string>> MoveFolderPathsAsync(string from, string to, int? tenantId = null, CancellationToken cancellationToken = default)
    {
        if (layers is null)
        {
            var settings = EditableSettings.From(options.CurrentValue);
            var changed = MoveFolderPaths(settings, from, to);
            if (changed.Count > 0)
            {
                Normalize(settings);
                await store.SaveAsync(settings, cancellationToken);
            }
            return changed;
        }

        var tenant = Tenant(tenantId);
        if (tenant is null)
            return [];
        var sources = await layers.LoadAsync(tenant, null, cancellationToken);
        var current = SettingLayers.ToSettings(SettingLayers.Resolve(sources, SettingLevel.Tenant));
        var keys = MoveFolderPaths(current, from, to);
        if (keys.Count == 0)
            return keys;

        Normalize(current);
        var values = SettingKeys.Flatten(SettingKeys.ToJson(current));
        var rows = sources.Tenant.ToDictionary(r => r.Key, r => r.Value, StringComparer.Ordinal);
        foreach (var key in keys)
            rows[key] = new StoredSetting(values[key], rows.GetValueOrDefault(key)?.Locked ?? false);
        await layers.Rows.SaveTenantAsync(tenant.Value, rows, cancellationToken);
        return keys;
    }

    /// <returns>The keys of the settings changed.</returns>
    private static List<string> MoveFolderPaths(EditableSettings settings, string from, string to)
    {
        var changed = new List<string>();
        if (Moved(settings.Scan.ExcludedFolders) is { } excluded)
        {
            settings.Scan.ExcludedFolders = excluded;
            changed.Add("scan.excludedFolders");
        }
        if (Moved(settings.Trickplay.Folders) is { } trickplay)
        {
            settings.Trickplay.Folders = trickplay;
            changed.Add("trickplay.folders");
        }
        if (settings.Editor.OutputFolder is { } output && Move(output) is { } moved)
        {
            settings.Editor.OutputFolder = moved;
            changed.Add("editor.outputFolder");
        }
        return changed;

        List<string>? Moved(List<string> folders)
        {
            var result = folders.Select(f => Move(f) ?? f).ToList();
            return result.SequenceEqual(folders, StringComparer.Ordinal) ? null : result;
        }

        // The new path of a folder at or below `from`; null for the others.
        string? Move(string folder) =>
            LibraryPaths.NormalizeRelative(folder) is { Length: > 0 } path && FolderPolicy.IsSameOrBelow(path, from)
                ? to + path[from.Length..]
                : null;
    }

    /// <summary>Back to the values of the configuration files.</summary>
    public SettingsView Reset()
    {
        store.Reset();
        layers?.NotifyChanged();
        return Get();
    }

    /// <summary>
    /// Forgets a level's values (and locks): the levels above apply again. Settings the person may not change keep
    /// their value.
    /// </summary>
    public async Task<SettingsView> ResetAsync(SettingLevel level, int? tenantId, int? userId, Func<SettingDefinition, bool>? canChange,
        CancellationToken cancellationToken = default)
    {
        if (layers is null || level == SettingLevel.Server)
            return Reset() with { Keys = layers is null ? null : SettingLayers.States(SettingLayers.Resolve(ServerSources(), SettingLevel.Server), SettingLevel.Server, canChange) };

        var sources = await layers.LoadAsync(Tenant(tenantId), userId, cancellationToken);
        if (level == SettingLevel.Tenant)
        {
            var tenant = Tenant(tenantId) ?? throw new InvalidOperationException("No library yet.");
            var kept = sources.Tenant.Where(r => SettingCatalog.Find(r.Key) is { } definition && canChange?.Invoke(definition) == false)
                .ToDictionary(r => r.Key, r => r.Value, StringComparer.Ordinal);
            await layers.Rows.SaveTenantAsync(tenant, kept, cancellationToken);
        }
        else if (userId is { } user)
        {
            await layers.Rows.SaveUserAsync(user, new Dictionary<string, JsonNode?>(), cancellationToken);
        }

        return await GetAsync(level, tenantId, userId, canChange, cancellationToken);
    }

    private static Dictionary<string, JsonNode?> Normalized(Dictionary<string, ResolvedSetting> resolved, Func<ResolvedSetting, JsonNode?> pick)
    {
        var json = new JsonObject();
        foreach (var (key, setting) in resolved)
            SettingKeys.Set(json, key, pick(setting));
        var settings = SettingKeys.FromJson(json);
        Normalize(settings);
        return SettingKeys.Flatten(SettingKeys.ToJson(settings));
    }

    /// <summary>The library named, else the one these services work for, else the first one.</summary>
    private int? Tenant(int? tenantId) => tenantId ?? binding?.TenantId ?? layers?.DefaultTenantId();

    private SettingsView View(SettingLevel level, SettingSources sources, Func<SettingDefinition, bool>? canChange)
    {
        var resolved = SettingLayers.Resolve(sources, level);
        var customized = level switch
        {
            SettingLevel.Server => store.IsCustomized,
            SettingLevel.Tenant => sources.Tenant.Count > 0,
            SettingLevel.User => sources.User.Count > 0,
            _ => false,
        };
        return new SettingsView(SettingLayers.ToSettings(resolved), customized, level, SettingLayers.States(resolved, level, canChange));
    }

    private SettingSources ServerSources() =>
        new(Environment(out var locks), locks, store.Values, store.Locked, new Dictionary<string, StoredSetting>(), new Dictionary<string, JsonNode?>());

    private IReadOnlyDictionary<string, JsonNode?> Environment(out IReadOnlyCollection<string> locks)
    {
        var configured = layers!.Configured();
        locks = configured.Locked;
        return SettingKeys.Flatten(SettingKeys.ToJson(EditableSettings.From(configured)));
    }

    private async Task WriteAsync(SettingLevel level, int? tenantId, int? userId, Dictionary<string, JsonNode?> values, SettingSources sources,
        CancellationToken cancellationToken)
    {
        switch (level)
        {
            case SettingLevel.Server:
                await store.SaveLayerAsync(values, store.Locked, cancellationToken);
                layers!.NotifyChanged();
                break;
            case SettingLevel.Tenant:
                var tenant = Tenant(tenantId) ?? throw new InvalidOperationException("No library yet.");
                // Locked settings keep their row (the lock pins the value) even when it equals the inherited one.
                var rows = values.ToDictionary(v => v.Key, v => new StoredSetting(v.Value, sources.Tenant.GetValueOrDefault(v.Key)?.Locked ?? false), StringComparer.Ordinal);
                await layers!.Rows.SaveTenantAsync(tenant, rows, cancellationToken);
                break;
            case SettingLevel.User:
                await layers!.Rows.SaveUserAsync(userId ?? throw new InvalidOperationException("No user."), values, cancellationToken);
                break;
            default:
                throw new InvalidOperationException("The environment level is set in the configuration only.");
        }
    }

    private static string Unchangeable(SettingDefinition definition, SettingState state, SettingLevel level) => state.LockedBy switch
    {
        SettingLevel.Environment => "Set by the server's configuration: it cannot be changed here.",
        SettingLevel.Server => "Locked by the server's settings: it cannot be changed here.",
        SettingLevel.Tenant => "Locked by the library's settings: it cannot be changed here.",
        _ when definition.PerLibrary && level == SettingLevel.Server => "Set for each library, in its own settings.",
        _ when level > definition.Scope => definition.Scope == SettingLevel.Server
            ? "A setting of the whole server: only server administrators change it."
            : "A setting of the library: only its administrators change it.",
        _ => "You don't have the right to change this setting.",
    };

    private static void Normalize(EditableSettings settings)
    {
        settings.DataServer = DataServerUrl.Normalize(settings.DataServer);
        settings.Reading.CustomFont = settings.Reading.CustomFont?.Trim() ?? string.Empty;
        settings.Player.ExternalName = string.IsNullOrWhiteSpace(settings.Player.ExternalName) ? null : settings.Player.ExternalName.Trim();
        settings.Player.ExternalUrl = string.IsNullOrWhiteSpace(settings.Player.ExternalUrl) ? null : settings.Player.ExternalUrl.Trim();
        settings.Scan.IgnoredDirectories = Names(settings.Scan.IgnoredDirectories);
        settings.Scan.IgnoredFiles = Names(settings.Scan.IgnoredFiles);
        settings.FileTypes.Images = Extensions(settings.FileTypes.Images);
        settings.FileTypes.Videos = Extensions(settings.FileTypes.Videos);
        settings.FileTypes.Audio = Extensions(settings.FileTypes.Audio);
        settings.Scan.ExcludedFolders = Folders(settings.Scan.ExcludedFolders);
        settings.Trickplay.Folders = Folders(settings.Trickplay.Folders);
        settings.Editor.OutputFolder = string.IsNullOrWhiteSpace(settings.Editor.OutputFolder)
            ? null
            : LibraryPaths.NormalizeRelative(settings.Editor.OutputFolder.Trim()) ?? settings.Editor.OutputFolder.Trim();

        static List<string> Names(IEnumerable<string>? names) => (names ?? [])
            .Select(n => n?.Trim())
            .OfType<string>()
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Kept as typed when invalid, so that the error shows next to the value.
        static List<string> Folders(IEnumerable<string>? folders) => Names(folders)
            .Select(f => LibraryPaths.NormalizeRelative(f) ?? f)
            .Where(f => f.Length > 0)
            .Distinct(LibraryPaths.PathComparer)
            .ToList();

        static List<string> Extensions(IEnumerable<string>? extensions) => Names((extensions ?? [])
            .Select(e => e?.Trim().TrimStart('.').ToLowerInvariant() ?? string.Empty));
    }

    private static Dictionary<string, string[]> Validate(EditableSettings s)
    {
        var errors = new Dictionary<string, string[]>();
        void Check(bool valid, string key, string message)
        {
            if (!valid && !errors.ContainsKey(key))
                errors[key] = [message];
        }

        void Range(int value, int min, int max, string key, string what) =>
            Check(value >= min && value <= max, key, $"{what} must be between {min} and {max}.");

        Check(s.DataServer is null || DataServerUrl.IsValid(s.DataServer),
            "dataServer", "The data server must be an http:// or https:// address (optionally with {url} or {encodedUrl}), or empty.");

        foreach (var (pages, key) in new (PageSettings, string)[] { (s.Browse, "browse"), (s.Search, "search") })
        {
            Range(pages.MaxPageSize, 1, MaxPageSize, $"{key}.maxPageSize", "The largest page");
            Range(pages.DefaultPageSize, 1, Math.Max(1, pages.MaxPageSize), $"{key}.defaultPageSize", "The page size");
        }

        Range(s.Browse.FolderPictures, 0, LibraryBrowser.MaxFolderPictures, "browse.folderPictures", "Pictures per folder");
        Range(s.Browse.ImageHighResolutionPixels, 1, MaxHighResolutionPixels, "browse.imageHighResolutionPixels", "The high resolution of photos");
        Range(s.Browse.VideoHighResolutionPixels, 1, MaxHighResolutionPixels, "browse.videoHighResolutionPixels", "The high resolution of videos");
        Range(s.Search.HistorySize, 0, MaxSearchHistory, "search.historySize", "Searches remembered");

        Range(s.Scan.Parallelism, 1, 64, "scan.parallelism", "Folders scanned at once");
        Check(s.Scan.IgnoredDirectories.All(IsName), "scan.ignoredDirectories", "Enter folder names, not paths.");
        Check(s.Scan.IgnoredFiles.All(IsName), "scan.ignoredFiles", "Enter file names, not paths.");
        var excluded = s.Scan.ExcludedFolders.FirstOrDefault(f => LibraryPaths.NormalizeRelative(f) is null);
        Check(excluded is null, "scan.excludedFolders", $"“{excluded}” is not a folder of the library: enter paths relative to the library, like Movies/Old.");

        foreach (var (list, key) in new[] { (s.FileTypes.Images, "fileTypes.images"), (s.FileTypes.Videos, "fileTypes.videos"), (s.FileTypes.Audio, "fileTypes.audio") })
            Check(list.All(e => e.All(c => char.IsLetterOrDigit(c) || c is '_' or '-')), key, "Extensions can only contain letters, digits, '-' and '_'.");
        var shared = s.FileTypes.Images.Intersect(s.FileTypes.Videos)
            .Concat(s.FileTypes.Images.Intersect(s.FileTypes.Audio))
            .Concat(s.FileTypes.Videos.Intersect(s.FileTypes.Audio))
            .FirstOrDefault();
        Check(shared is null, "fileTypes", $"“{shared}” is in two lists: an extension can only be one kind of media.");

        Range(s.Thumbnails.Width, 32, 4096, "thumbnails.width", "The thumbnail width");
        Range(s.Thumbnails.LargeImageKilobytes, 0, MaxLargeImageKilobytes, "thumbnails.largeImageKilobytes", "The large image size");
        Range(s.Thumbnails.VideoSeekSeconds, 0, 3600, "thumbnails.videoSeekSeconds", "The video position");
        Range(s.Thumbnails.MaxConcurrency, 1, 32, "thumbnails.maxConcurrency", "Thumbnails rendered at once");
        Range(s.Thumbnails.TimeoutSeconds, 1, 600, "thumbnails.timeoutSeconds", "The time limit");

        var outside = s.Trickplay.Folders.FirstOrDefault(f => LibraryPaths.NormalizeRelative(f) is null);
        Check(outside is null, "trickplay.folders", $"“{outside}” is not a folder of the library: enter paths relative to the library, like Movies/Series.");
        Range(s.Trickplay.IntervalSeconds, 1, 600, "trickplay.intervalSeconds", "The interval");
        Range(s.Trickplay.Width, 64, 1280, "trickplay.width", "The frame width");
        Range(s.Trickplay.Columns, 1, 20, "trickplay.columns", "Columns");
        Range(s.Trickplay.Rows, 1, 20, "trickplay.rows", "Rows");
        Range(s.Trickplay.Parallelism, 1, 16, "trickplay.parallelism", "Videos processed at once");
        Range(s.Trickplay.TimeoutMinutes, 1, 1440, "trickplay.timeoutMinutes", "The time limit");
        Range(s.Trickplay.RecheckHours, 0, 720, "trickplay.recheckHours", "The time between checks");

        Range(s.Tags.SidebarCount, 1, 20, "tags.sidebarCount", "Tags per section");
        Range(s.Player.SeekSeconds, 1, 600, "player.seekSeconds", "The jump");
        Check(s.Player.ExternalUrl is null || (s.Player.ExternalUrl.Length <= MaxExternalUrlLength && PlayerOptions.IsAllowedExternalUrl(s.Player.ExternalUrl)),
            "player.externalUrl", $"Enter a link starting with the player's scheme, like vlc://{{url}} (at most {MaxExternalUrlLength} characters), or leave it empty.");
        Range(s.Player.LinkLifetimeMinutes, 1, MaxLinkLifetimeMinutes, "player.linkLifetimeMinutes", "The link lifetime");
        Check(s.Player.ExternalName is null || s.Player.ExternalName.Length <= 50, "player.externalName", "The name can be at most 50 characters.");
        Check(Enum.IsDefined(s.Streaming.Mode), "streaming.mode", "Choose when videos are converted from the list.");
        Range(s.Streaming.MaxStreams, 1, MaxStreams, "streaming.maxStreams", "Videos converted at once");
        Range(s.Streaming.Quality, MinStreamQuality, MaxStreamQuality, "streaming.quality", "The quality");
        Check(s.Streaming.MaxHeight == 0 || s.Streaming.MaxHeight is >= MinStreamHeight and <= MaxStreamHeight, "streaming.maxHeight",
            $"The largest height must be between {MinStreamHeight} and {MaxStreamHeight} pixels, or 0 to keep the video's.");
        Range(s.FileTracking.Duplicates.MinimumKilobytes, 0, MaxLargeImageKilobytes, "fileTracking.duplicates.minimumKilobytes", "The smallest size");
        Range(s.FileTracking.Duplicates.GroupsPerPage, 1, FileTracking.DuplicateFinder.MaxGroupsPerPage, "fileTracking.duplicates.groupsPerPage", "Groups per page");
        Range(s.FileTracking.Relink.RetentionDays, 0, MaxRetentionDays, "fileTracking.relink.retentionDays", "The days missing files are kept");
        Range(s.FileTracking.SimilarVideos.MinimumPercent, FileTracking.SimilarVideoFinder.LowestMinimumPercent, 100, "fileTracking.similarVideos.minimumPercent", "The match");
        Range(s.Tasks.LogRetentionDays, 0, MaxRetentionDays, "tasks.logRetentionDays", "The days task logs are kept");
        Range(s.Logs.RetentionDays, 0, MaxRetentionDays, "logs.retentionDays", "The days log entries are kept");
        Check(s.Editor.OutputFolder is null || LibraryPaths.NormalizeRelative(s.Editor.OutputFolder) is not null,
            "editor.outputFolder", $"“{s.Editor.OutputFolder}” is not a folder of the library: enter a path relative to the library, like Edited or Photos/Edited, or leave it empty.");
        Check(s.Editor.OutputFolder is null || s.Editor.OutputFolder.Length <= MaxEditorFolderLength,
            "editor.outputFolder", $"The folder can be at most {MaxEditorFolderLength} characters.");
        Range(s.Editor.Quality, Editing.EditedImages.MinQuality, 100, "editor.quality", "The quality");
        Range(s.Files.Trash.RetentionDays, 0, MaxRetentionDays, "files.trash.retentionDays", "The days in the trash");
        Range(s.Files.MinimumFreeSpaceMegabytes, 0, 1_048_576, "files.minimumFreeSpaceMegabytes", "The free space kept");
        Range(s.Files.Uploads.MaxFileSizeMegabytes, 0, 1_073_741_823, "files.uploads.maxFileSizeMegabytes", "The largest upload");
        Range(s.Files.Uploads.KeepUnfinishedHours, 1, 8760, "files.uploads.keepUnfinishedHours", "The hours unfinished uploads are kept");
        Range(s.Files.MaxArchiveMegabytes, 0, 1_073_741_823, "files.maxArchiveMegabytes", "The largest zip");
        Check(Enum.IsDefined(s.Appearance.Theme), "appearance.theme", "Choose a theme from the list.");
        Check(s.Reading.Theme is { } readerTheme && ReadingOptions.Themes.Contains(readerTheme), "reading.theme", "Choose colours from the list.");
        Check(s.Reading.CustomFont.Length <= MaxCustomFontLength && s.Reading.CustomFont.IndexOfAny(['"', '\\', ';', '{', '}', '<', '>']) < 0,
            "reading.customFont", $"Enter a font's name (at most {MaxCustomFontLength} characters, without quotes or brackets).");
        foreach (var (profile, key) in new[] { (s.Reading.Code, "reading.code"), (s.Reading.Prose, "reading.prose") })
        {
            Check(profile.Font is { } font && ReadingProfileOptions.Fonts.Contains(font), $"{key}.font", "Choose a font from the list.");
            Range(profile.Size, ReadingProfileOptions.MinSize, ReadingProfileOptions.MaxSize, $"{key}.size", "The text size");
            Check(ReadingProfileOptions.LineHeights.Any(height => Math.Abs(height - profile.LineHeight) < 0.001), $"{key}.lineHeight", "Choose a line height from the list.");
            Check(ReadingProfileOptions.Widths.Contains(profile.Width), $"{key}.width", "Choose a width from the list.");
            Check(ReadingProfileOptions.Paddings.Contains(profile.Padding), $"{key}.padding", "Choose margins from the list.");
        }
        return errors;

        static bool IsName(string name) => name.IndexOfAny(['/', '\\']) < 0;
    }
}
