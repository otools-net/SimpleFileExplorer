using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Sfe.Core.Options;
using Sfe.Core.Paths;
using Sfe.Core.Thumbnails;

namespace Sfe.Api.Maintenance;

/// <param name="Value">The value the server uses now (from whichever source gives it).</param>
/// <param name="Locked">A source after the installation file gives it (an environment variable, the command line...): the page can't change it.</param>
/// <param name="LockedBy">That source, in words.</param>
public sealed record InstallationField(string? Value, bool Locked, string? LockedBy);

/// <param name="Root">The first library's folder (<c>Sfe:Root</c>).</param>
/// <param name="RootExists">That folder exists on the server.</param>
/// <param name="Urls">The listening addresses (<c>Urls</c>).</param>
/// <param name="Port">The port of the first address.</param>
/// <param name="Network">The first address listens on every network interface, not only this computer's.</param>
/// <param name="FfmpegPath">ffmpeg as configured (<c>Sfe:Thumbnails:FfmpegPath</c>).</param>
/// <param name="FfmpegFound">The executable found for it; null when there is none.</param>
/// <param name="FfmpegVersion">The version it reports.</param>
/// <param name="RestartRequired">The addresses saved differ from those the server listens on since it started.</param>
public sealed record InstallationView(
    InstallationField Root, bool RootExists,
    InstallationField Urls, int Port, bool Network,
    InstallationField FfmpegPath, string? FfmpegFound, string? FfmpegVersion,
    bool RestartRequired);

/// <param name="Root">The first library's folder; null keeps it.</param>
/// <param name="Network">Listen on every network interface (true) or on this computer only (false); null keeps it.</param>
/// <param name="Port">The port; null keeps it.</param>
/// <param name="FfmpegPath">ffmpeg's executable or folder; empty = look for it on PATH; null keeps it.</param>
public sealed record InstallationInput(string? Root = null, bool? Network = null, int? Port = null, string? FfmpegPath = null);

/// <summary>
/// The few settings of the installation that a person setting SFE up needs, written from the web (with the setup code)
/// into an app-managed configuration file (<see cref="FileKey"/>, default <c>data/installation.json</c>), so that nobody
/// has to edit JSON by hand. That file comes after <c>appsettings*.json</c> and before the environment and the command
/// line (Program.cs): what whoever runs the server sets there still wins, and shows as locked. Only <see cref="Keys"/>
/// may be written; the database, caches, keys and security stay in the configuration files.
/// </summary>
public sealed class InstallationSettings
{
    public const string FileKey = $"{SfeOptions.SectionName}:InstallationFile";
    public const string DefaultFile = "data/installation.json";

    public const string RootKey = $"{SfeOptions.SectionName}:{nameof(SfeOptions.Root)}";
    public const string UrlsKey = "Urls";
    public const string FfmpegKey = $"{SfeOptions.SectionName}:{nameof(SfeOptions.Thumbnails)}:{nameof(ThumbnailOptions.FfmpegPath)}";

    /// <summary>The keys the file may hold.</summary>
    public static readonly IReadOnlyList<string> Keys = [RootKey, UrlsKey, FfmpegKey];

    /// <summary>Kestrel's default when nothing sets <c>Urls</c>.</summary>
    private const string DefaultUrls = "http://localhost:5000";

    private readonly IConfiguration _configuration;
    private readonly string _startUrls;
    private readonly Lock _lock = new();

    public InstallationSettings(IConfiguration configuration, IWebHostEnvironment environment)
    {
        _configuration = configuration;
        FilePath = ResolvePath(configuration, environment.ContentRootPath);
        _startUrls = configuration[UrlsKey] ?? DefaultUrls;
    }

    /// <summary>Full path of the file.</summary>
    public string FilePath { get; }

    /// <summary>Full path of the file from a configuration (Program.cs adds it before the host exists).</summary>
    public static string ResolvePath(IConfiguration configuration, string contentRootPath) =>
        Path.GetFullPath(LibraryPaths.ExpandHome(configuration[FileKey] is { Length: > 0 } file ? file : DefaultFile), contentRootPath);

    /// <summary>
    /// Inserts the file in <paramref name="configuration"/> right after the last <c>appsettings*.json</c>: before the
    /// environment variables and the command line, which keep the last word.
    /// </summary>
    public static void AddTo(IConfigurationBuilder configuration, string path)
    {
        var source = new JsonConfigurationSource { Path = path, Optional = true, ReloadOnChange = true };
        // Watches the nearest existing folder: the data folder may not exist yet.
        source.ResolveFileProvider();
        configuration.Sources.Insert(AfterAppSettings(configuration), source);
    }

    /// <summary>Where a source after the <c>appsettings*.json</c> files goes.</summary>
    public static int AfterAppSettings(IConfigurationBuilder configuration)
    {
        var last = -1;
        for (var i = 0; i < configuration.Sources.Count; i++)
        {
            if (configuration.Sources[i] is JsonConfigurationSource { Path: { } path }
                && Path.GetFileName(path).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase))
                last = i;
        }

        return last >= 0 ? last + 1 : 0;
    }

    public async Task<InstallationView> DescribeAsync(CancellationToken cancellationToken = default)
    {
        var root = Field(RootKey);
        var urls = Field(UrlsKey) is { Value: null } unset ? unset with { Value = DefaultUrls } : Field(UrlsKey);
        var (port, network) = ParseUrls(urls.Value);
        var ffmpeg = Field(FfmpegKey) is { Value: null } noFfmpeg ? noFfmpeg with { Value = new ThumbnailOptions().FfmpegPath } : Field(FfmpegKey);
        var found = Ffmpeg.Find(ffmpeg.Value);
        var version = found is null ? null : await Ffmpeg.ProbeVersionAsync(found, cancellationToken);
        var rootPath = root.Value is { Length: > 0 } configured ? Full(configured) : null;
        return new InstallationView(
            root with { Value = rootPath ?? root.Value }, rootPath is not null && Directory.Exists(rootPath),
            urls, port, network,
            ffmpeg, found, version,
            !string.Equals(urls.Value, _startUrls, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Checks and writes the changed values.</summary>
    /// <returns>The errors by field (<c>root</c>, <c>port</c>, <c>ffmpegPath</c>), or the keys that changed.</returns>
    public (Dictionary<string, string[]>? Errors, IReadOnlyList<string> Changed) Save(InstallationInput input)
    {
        var errors = new Dictionary<string, string[]>();
        var values = new Dictionary<string, string?>();

        if (input.Root is not null)
        {
            var root = input.Root.Trim() is { Length: > 0 } typed ? Full(typed) : null;
            if (root is null || !Directory.Exists(root))
                errors["root"] = ["This folder does not exist on the server."];
            else if (!CanList(root))
                errors["root"] = ["SFE cannot read this folder: check its permissions."];
            else
                values[RootKey] = root;
        }

        if (input.Network is not null || input.Port is not null)
        {
            var (port, network) = ParseUrls(_configuration[UrlsKey] ?? DefaultUrls);
            port = input.Port ?? port;
            network = input.Network ?? network;
            if (port is < 1 or > 65535)
                errors["port"] = ["A port is a number from 1 to 65535."];
            else
                values[UrlsKey] = $"http://{(network ? "*" : "localhost")}:{port}";
        }

        if (input.FfmpegPath is not null)
        {
            var typed = input.FfmpegPath.Trim();
            if (typed.Length == 0)
            {
                values[FfmpegKey] = null;
            }
            else if (Ffmpeg.Find(LibraryPaths.ExpandHome(typed)) is { } found)
            {
                values[FfmpegKey] = found;
            }
            else
            {
                errors["ffmpegPath"] = ["No ffmpeg there: pick its program, or the folder it is in."];
            }
        }

        foreach (var key in values.Keys)
        {
            if (Field(key) is { Locked: true, LockedBy: var by })
                errors[FieldName(key)] = [$"Set by {by}: change it there."];
        }

        if (errors.Count > 0)
            return (errors, []);

        var changed = values.Where(v => !string.Equals(v.Value, _configuration[v.Key], StringComparison.Ordinal)).Select(v => v.Key).ToList();
        if (changed.Count > 0)
            Write(values);
        return (null, changed);
    }

    /// <summary>The value of a key and whether a source after the file decides it.</summary>
    private InstallationField Field(string key)
    {
        var value = _configuration[key];
        if (_configuration is not IConfigurationRoot root)
            return new InstallationField(value, false, null);

        var providers = root.Providers.ToList();
        var ours = providers.FindIndex(IsOurs);
        for (var i = providers.Count - 1; i > ours && ours >= 0; i--)
        {
            if (providers[i].TryGet(key, out _))
                return new InstallationField(value, true, Describe(providers[i]));
        }

        return new InstallationField(value, false, null);
    }

    private bool IsOurs(IConfigurationProvider provider) =>
        provider is JsonConfigurationProvider { Source: { Path: { } path, FileProvider: { } files } }
        && files.GetFileInfo(path).PhysicalPath is { } physical
        && string.Equals(Path.GetFullPath(physical), FilePath, StringComparison.OrdinalIgnoreCase);

    private static string Describe(IConfigurationProvider provider) => provider switch
    {
        EnvironmentVariablesConfigurationProvider => "an environment variable of the server",
        CommandLineConfigurationProvider => "the server's command line",
        _ => "the server's configuration",
    };

    private static string FieldName(string key) => key switch
    {
        RootKey => "root",
        UrlsKey => "port",
        _ => "ffmpegPath",
    };

    /// <summary>Merges <paramref name="values"/> into the file (null removes a key) and reloads it at once.</summary>
    private void Write(Dictionary<string, string?> values)
    {
        lock (_lock)
        {
            JsonObject file;
            try
            {
                file = File.Exists(FilePath) ? JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject ?? [] : [];
            }
            catch (JsonException)
            {
                // Only this page writes it: a damaged one is started again.
                file = [];
            }

            foreach (var (key, value) in values)
                SetPath(file, key.Split(':'), value);

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, file.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, FilePath, overwrite: true);

            // The file watcher would reload it a moment later: the caller restarts the libraries now.
            if (_configuration is IConfigurationRoot root)
            {
                foreach (var provider in root.Providers.Where(IsOurs))
                    provider.Load();
            }
        }
    }

    private static void SetPath(JsonObject node, string[] path, string? value)
    {
        for (var i = 0; i < path.Length - 1; i++)
        {
            if (node[path[i]] is not JsonObject child)
            {
                if (value is null)
                    return;
                node[path[i]] = child = [];
            }

            node = child;
        }

        if (value is null)
            node.Remove(path[^1]);
        else
            node[path[^1]] = value;
    }

    /// <summary>The port of the first address, and whether it listens beyond this computer.</summary>
    internal static (int Port, bool Network) ParseUrls(string? urls)
    {
        var first = (urls ?? DefaultUrls).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? DefaultUrls;
        // "http://*:5080" and "http://+:5080" aren't valid URIs: read host and port by hand.
        var rest = first.Contains("://", StringComparison.Ordinal) ? first[(first.IndexOf("://", StringComparison.Ordinal) + 3)..] : first;
        rest = rest.Split('/', 2)[0];
        var colon = rest.LastIndexOf(':');
        var host = colon > 0 && !rest.EndsWith(']') ? rest[..colon] : rest;
        var port = colon > 0 && int.TryParse(rest[(colon + 1)..], out var parsed) ? parsed : first.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80;
        var local = host is "localhost" or "127.0.0.1" or "[::1]";
        return (port, !local);
    }

    private static string Full(string path) => LibraryPaths.NormalizeRoot(path);

    private static bool CanList(string folder)
    {
        try
        {
            using var entries = Directory.EnumerateFileSystemEntries(folder).GetEnumerator();
            entries.MoveNext();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
