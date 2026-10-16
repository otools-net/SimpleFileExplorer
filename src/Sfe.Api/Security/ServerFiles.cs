using Microsoft.Data.Sqlite;
using Sfe.Core;
using Sfe.Core.Data;
using Sfe.Core.Options;
using Sfe.Core.Paths;

namespace Sfe.Api.Security;

/// <summary>
/// The server's own files, never served as files of a library: the keys signing the sessions (with them anyone forges an
/// administrator's cookie), the setup code, the databases, the configuration. A library reaches them when its folder holds
/// SFE's (a root at <c>C:\</c>), or through a folder link (symbolic link, junction) someone made in it: paths are checked
/// once every link on the way is resolved (<see cref="LibraryPaths.RealPath"/>).
/// </summary>
public sealed class ServerFiles
{
    private readonly Lazy<(string[] Folders, string[] Files)> _protected;

    public ServerFiles(IConfiguration configuration, IWebHostEnvironment environment)
    {
        _protected = new(() => Collect(configuration, environment.ContentRootPath));
    }

    /// <summary><paramref name="fullPath"/> is one of the server's files, or in one of its folders, wherever links lead.</summary>
    public bool Contains(string fullPath)
    {
        var real = LibraryPaths.RealPath(fullPath);
        var (folders, files) = _protected.Value;
        return folders.Any(folder => LibraryPaths.IsSameOrInside(real, folder))
            || files.Any(file => string.Equals(real, file, LibraryPaths.PathComparison));
    }

    private static (string[] Folders, string[] Files) Collect(IConfiguration configuration, string contentRoot)
    {
        string Full(string path) => Path.GetFullPath(LibraryPaths.ExpandHome(path), contentRoot);
        string Setting(string key, string fallback) => configuration[key] is { Length: > 0 } value ? value : fallback;
        var section = SfeOptions.SectionName;

        // The program's folder holds appsettings*.json (connection strings, maybe the setup code) and, by default, data/.
        List<string> folders =
        [
            contentRoot,
            Full(Setting($"{section}:{nameof(SfeOptions.Security)}:{nameof(SecurityOptions.KeysDirectory)}", new SecurityOptions().KeysDirectory)),
            Full(Setting($"{section}:{nameof(SfeOptions.Logs)}:{nameof(LogOptions.Directory)}", new LogOptions().Directory)),
        ];
        var codeFile = Full(Setting($"{section}:{nameof(SfeOptions.Setup)}:{nameof(SetupOptions.CodeFile)}", new SetupOptions().CodeFile));
        List<string> files =
        [
            codeFile,
            // AccountRecovery's codes, beside the setup code.
            Path.Combine(Path.GetDirectoryName(codeFile)!, "recovery-code.txt"),
            Full(Setting(Maintenance.InstallationSettings.FileKey, Maintenance.InstallationSettings.DefaultFile)),
            Full(Setting($"{section}:{nameof(SfeOptions.SettingsFile)}", new SfeOptions().SettingsFile)),
        ];

        var provider = configuration.GetSection($"{section}:{nameof(SfeOptions.Database)}").Get<DatabaseOptions>()?.Provider ?? DatabaseProvider.SqlServer;
        if (provider == DatabaseProvider.Sqlite && configuration.GetConnectionString(ServiceCollectionExtensions.ConnectionStringName) is { Length: > 0 } main)
        {
            var database = new SqliteConnectionStringBuilder(SfeDatabase.PrepareSqlite(main, contentRoot)).DataSource;
            if (!string.IsNullOrEmpty(database) && database != ":memory:")
            {
                var full = Full(database);
                files.AddRange([full, full + "-wal", full + "-shm", full + "-journal"]);
                // The other libraries' indexes (TenantHost.ConnectionString).
                folders.Add(Path.Combine(Path.GetDirectoryName(full) ?? contentRoot, "tenants"));
            }
        }

        return ([.. folders.Select(LibraryPaths.RealPath)], [.. files.Select(LibraryPaths.RealPath)]);
    }
}
