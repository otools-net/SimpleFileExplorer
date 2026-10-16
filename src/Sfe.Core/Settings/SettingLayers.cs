using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Sfe.Core.Access;
using Sfe.Core.Options;

namespace Sfe.Core.Settings;

/// <summary>The values of every level for one library and one user, as stored.</summary>
/// <param name="Environment">The configuration's values (and the code's defaults).</param>
/// <param name="EnvironmentLocks">Patterns of <see cref="SfeOptions.Locked"/>.</param>
public sealed record SettingSources(
    IReadOnlyDictionary<string, JsonNode?> Environment,
    IReadOnlyCollection<string> EnvironmentLocks,
    IReadOnlyDictionary<string, JsonNode?> Server,
    IReadOnlyCollection<string> ServerLocks,
    IReadOnlyDictionary<string, StoredSetting> Tenant,
    IReadOnlyDictionary<string, JsonNode?> User);

/// <param name="Value">In effect at the level asked for.</param>
/// <param name="Source">The level it comes from.</param>
/// <param name="LockedAt">The level that locked it (the levels below cannot change it); null = not locked.</param>
/// <param name="Inherited">What the level asked for gets from the levels above, without a value of its own.</param>
public sealed record ResolvedSetting(JsonNode? Value, SettingLevel Source, SettingLevel? LockedAt, JsonNode? Inherited);

/// <summary>How a setting shows on the settings page of a level.</summary>
/// <param name="Scope">The most specific level that may set it (<see cref="SettingDefinition.Scope"/>).</param>
/// <param name="Source">Where its value comes from.</param>
/// <param name="Editable">This level may change it: within its scope, not locked by a level above, and (for a library's administrator) with the right.</param>
/// <param name="Lockable">This level may lock it for the levels below (only when one of them could change it).</param>
/// <param name="Locked">This level locked it.</param>
/// <param name="LockedBy">The level above that locked it, when one did.</param>
/// <param name="Inherited">The value of the levels above, which applies without one of this level.</param>
public sealed record SettingState(SettingLevel Scope, SettingLevel Source, bool Editable, bool Lockable, bool Locked, SettingLevel? LockedBy, JsonNode? Inherited);

/// <summary>
/// The settings by level (<see cref="SettingLevel"/>): the configuration's, then the server settings page's
/// (<see cref="SettingsStore"/>), then a library's and a user's (<see cref="SettingRows"/>). Each level applies to the
/// settings its scope reaches (<see cref="SettingCatalog"/>) and that no level above locked. A library's services see
/// the levels down to the library's (<see cref="ApplyTo"/>); a user's preferences are only applied for the web client.
/// </summary>
public sealed class SettingLayers : IOptionsChangeTokenSource<SfeOptions>
{
    private readonly IConfiguration _configuration;
    private readonly SettingsStore _server;
    private readonly SettingRows _rows;
    private readonly ILogger<SettingLayers> _logger;
    private CancellationTokenSource _changed = new();

    public SettingLayers(IConfiguration configuration, SettingsStore server, SettingRows rows, AccessChanges changes, ILogger<SettingLayers> logger)
    {
        _configuration = configuration;
        _server = server;
        _rows = rows;
        _logger = logger;
        changes.TenantSettingsChanged += _ => NotifyChanged();
    }

    public SettingsStore Server => _server;
    public SettingRows Rows => _rows;

    public string? Name => Microsoft.Extensions.Options.Options.DefaultName;

    public IChangeToken GetChangeToken() => new CancellationChangeToken(_changed.Token);

    /// <summary>
    /// The settings of the configuration alone (the environment level), with the defaults the options add to it.
    /// The same for every library: what differs between libraries (root, index) is not a setting of the web client.
    /// </summary>
    public SfeOptions Configured()
    {
        var options = new SfeOptions();
        var section = _configuration.GetSection(SfeOptions.SectionName);
        section.Bind(options);
        options.FileTypes.ApplyDefaults(list => section.GetSection($"{nameof(SfeOptions.FileTypes)}:{list}").Exists());
        return options;
    }

    /// <summary>The library the services of the configuration belong to, when they don't name one.</summary>
    public int? DefaultTenantId() => _rows.DefaultTenantId();

    /// <summary>
    /// Applies the server's and the library's levels over <paramref name="options"/>, which hold the configuration's
    /// values (a post-configuration of the library's options).
    /// </summary>
    public void ApplyTo(SfeOptions options, int? tenantId)
    {
        try
        {
            var environment = SettingKeys.Flatten(SettingKeys.ToJson(EditableSettings.From(options)));
            var tenant = tenantId ?? _rows.DefaultTenantId();
            var sources = new SettingSources(environment, options.Locked, _server.Values, _server.Locked,
                tenant is { } id ? _rows.ForTenant(id) : new Dictionary<string, StoredSetting>(), new Dictionary<string, JsonNode?>());
            ToSettings(Resolve(sources, SettingLevel.Tenant)).ApplyTo(options);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            _logger.LogWarning(ex, "Ignoring the saved settings: they don't match the expected format");
        }
    }

    /// <summary>
    /// The settings in effect for the whole server: the configuration's, then the server settings page's, without
    /// reading the database (what the server's own services use, e.g. the log, also while the database is down).
    /// </summary>
    public SfeOptions ServerLevel()
    {
        var options = Configured();
        try
        {
            var sources = new SettingSources(SettingKeys.Flatten(SettingKeys.ToJson(EditableSettings.From(options))), options.Locked,
                _server.Values, _server.Locked, new Dictionary<string, StoredSetting>(), new Dictionary<string, JsonNode?>());
            ToSettings(Resolve(sources, SettingLevel.Server)).ApplyTo(options);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            _logger.LogWarning(ex, "Ignoring the saved settings: they don't match the expected format");
        }

        return options;
    }

    public async Task<SettingSources> LoadAsync(int? tenantId, int? userId, CancellationToken cancellationToken = default)
    {
        var configured = Configured();
        var tenant = tenantId ?? _rows.DefaultTenantId();
        return new SettingSources(
            SettingKeys.Flatten(SettingKeys.ToJson(EditableSettings.From(configured))),
            configured.Locked,
            _server.Values,
            _server.Locked,
            tenant is { } id ? _rows.ForTenant(id) : new Dictionary<string, StoredSetting>(),
            userId is { } user ? await _rows.ForUserAsync(user, cancellationToken) : new Dictionary<string, JsonNode?>());
    }

    /// <summary>Every setting in effect at <paramref name="level"/>, with where it comes from.</summary>
    public static Dictionary<string, ResolvedSetting> Resolve(SettingSources sources, SettingLevel level)
    {
        var resolved = new Dictionary<string, ResolvedSetting>(StringComparer.Ordinal);
        foreach (var definition in SettingCatalog.All)
        {
            var key = definition.Key;
            var value = sources.Environment.GetValueOrDefault(key);
            var source = SettingLevel.Environment;
            SettingLevel? lockedAt = sources.EnvironmentLocks.Any(p => SettingCatalog.Matches(p, key)) ? SettingLevel.Environment : null;
            var inherited = value;

            // Each level in turn: what it inherits is the value before its own applies.
            for (var current = SettingLevel.Server; current <= level; current++)
            {
                if (current == level)
                    inherited = value;
                if (lockedAt is not null || current > definition.Scope || (current == SettingLevel.Server && definition.PerLibrary))
                    continue;

                switch (current)
                {
                    case SettingLevel.Server:
                        if (sources.Server.TryGetValue(key, out var server))
                            (value, source) = (server, SettingLevel.Server);
                        if (definition.Scope > SettingLevel.Server && sources.ServerLocks.Contains(key))
                            lockedAt = SettingLevel.Server;
                        break;
                    case SettingLevel.Tenant when sources.Tenant.TryGetValue(key, out var tenant):
                        (value, source) = (tenant.Value, SettingLevel.Tenant);
                        if (tenant.Locked && definition.Scope > SettingLevel.Tenant)
                            lockedAt = SettingLevel.Tenant;
                        break;
                    case SettingLevel.User when sources.User.TryGetValue(key, out var user):
                        (value, source) = (user, SettingLevel.User);
                        break;
                }
            }

            resolved[key] = new ResolvedSetting(value, source, lockedAt, inherited);
        }

        return resolved;
    }

    /// <summary>How each setting shows on the page of <paramref name="level"/>.</summary>
    /// <param name="canChange">Whether the person may change a setting at all (their rights); null = every one.</param>
    public static Dictionary<string, SettingState> States(Dictionary<string, ResolvedSetting> resolved, SettingLevel level, Func<SettingDefinition, bool>? canChange = null)
    {
        var states = new Dictionary<string, SettingState>(StringComparer.Ordinal);
        foreach (var definition in SettingCatalog.All)
        {
            var setting = resolved[definition.Key];
            var lockedAbove = setting.LockedAt is { } at && at < level ? at : (SettingLevel?)null;
            var inScope = level <= definition.Scope && !(definition.PerLibrary && level == SettingLevel.Server);
            var editable = inScope && lockedAbove is null && level > SettingLevel.Environment && (canChange?.Invoke(definition) ?? true);
            states[definition.Key] = new SettingState(
                definition.Scope,
                setting.Source,
                editable,
                editable && level < definition.Scope,
                setting.LockedAt == level,
                lockedAbove,
                setting.Inherited);
        }

        return states;
    }

    public static EditableSettings ToSettings(Dictionary<string, ResolvedSetting> resolved)
    {
        var json = new JsonObject();
        foreach (var (key, setting) in resolved)
            SettingKeys.Set(json, key, setting.Value);
        return SettingKeys.FromJson(json);
    }

    /// <summary>
    /// Settings that hold paths inside a library (<see cref="SettingDefinition.PerLibrary"/>) were server settings
    /// before libraries existed: they move to the first library, whose folders they name, and leave the settings file.
    /// </summary>
    public async Task MovePerLibrarySettingsAsync(int defaultTenantId, CancellationToken cancellationToken = default)
    {
        var saved = _server.Values;
        var perLibrary = SettingCatalog.All.Where(d => d.PerLibrary && saved.ContainsKey(d.Key)).ToList();
        if (perLibrary.Count == 0)
            return;

        var environment = SettingKeys.Flatten(SettingKeys.ToJson(EditableSettings.From(Configured())));
        var rows = _rows.ForTenant(defaultTenantId).ToDictionary(r => r.Key, r => r.Value, StringComparer.Ordinal);
        foreach (var definition in perLibrary.Where(d => !rows.ContainsKey(d.Key) && !SettingKeys.Same(saved[d.Key], environment.GetValueOrDefault(d.Key))))
            rows[definition.Key] = new StoredSetting(saved[definition.Key], false);
        await _rows.SaveTenantAsync(defaultTenantId, rows, cancellationToken);

        var remaining = saved.Where(v => perLibrary.All(d => d.Key != v.Key)).ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);
        await _server.SaveLayerAsync(remaining, _server.Locked, cancellationToken);
        NotifyChanged();
        _logger.LogWarning("Moved the settings {Settings} from {File} to the first library's own settings", string.Join(", ", perLibrary.Select(d => d.Key)), _server.FilePath);
    }

    /// <summary>
    /// Sets one setting at the server level, outside the settings page (the setup's choices): kept only when it
    /// differs from the configuration's value. Nothing changes when the configuration locks it.
    /// </summary>
    /// <returns>The value applies now.</returns>
    public async Task<bool> SaveServerValueAsync(string key, JsonNode? value, CancellationToken cancellationToken = default)
    {
        var configured = Configured();
        if (SettingCatalog.Find(key) is null || configured.Locked.Any(pattern => SettingCatalog.Matches(pattern, key)))
            return false;

        var environment = SettingKeys.Flatten(SettingKeys.ToJson(EditableSettings.From(configured)));
        var values = _server.Values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);
        if (SettingKeys.Same(value, environment.GetValueOrDefault(key)) && !_server.Locked.Contains(key))
            values.Remove(key);
        else
            values[key] = value?.DeepClone();
        await _server.SaveLayerAsync(values, _server.Locked, cancellationToken);
        NotifyChanged();
        return true;
    }

    /// <summary>Signals the libraries' options after a save of the server level (the libraries' own saves signal through <see cref="AccessChanges"/>).</summary>
    public void NotifyChanged()
    {
        var previous = Interlocked.Exchange(ref _changed, new CancellationTokenSource());
        previous.Cancel();
        previous.Dispose();
    }
}
