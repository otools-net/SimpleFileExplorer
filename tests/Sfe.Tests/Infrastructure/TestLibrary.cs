using Microsoft.Data.Sqlite;
using Sfe.Core.Data;

namespace Sfe.Tests.Infrastructure;

/// <summary>
/// Temporary folder tree with odd and Unicode names, plus a few media files,
/// and a SQLite index file next to it.
/// </summary>
public sealed class TestLibrary : IDisposable
{
    public const string SpecialFolder = "S̷͎̿͐ĕ̴̗͊ͅṗ̵̗͓̋ĉ̵̺̚i̸͉̿̓ȧ̴̙l̴̙̊ ̸̜̺͑F̶̠̓̅o̵̗̊͝l̴̡̗͊d̷͍̱͋e̷̙̼͂r̴̘̈́̄";

    /// <summary>The setup code of the HTTP tests' servers (<c>Sfe:Setup:Code</c>).</summary>
    public const string SetupCode = "test-setup-code-0123";

    private const int Kb = 1024;
    private const int Mb = 1024 * Kb;

    public TestLibrary()
    {
        BasePath = Path.Combine(Path.GetTempPath(), "sfe-tests", Guid.NewGuid().ToString("N"));
        Root = Path.Combine(BasePath, "Root");
        DatabasePath = Path.Combine(BasePath, "index.db");
        ThumbnailPath = Path.Combine(BasePath, "thumbs");

        AddFile("RFile1.txt", 10);
        AddFile("RFile2", 2 * Kb);
        AddFile("100%_done[1].txt", 10);
        AddFile("ChildDir/AFile1.txt", 10);
        AddFile("ChildDir/AFile2", 10);
        AddFile("RepeatedDir/RDFile1.txt", 10);
        AddFile("RepeatedDir/RDFile2.txt", 10);
        AddFile("RepeatedDir/ChildDir/AFile1.txt", 10);
        AddFile("RepeatedDir/ChildDir/AFile2", 10);
        AddFile("RepeatedDir/RepeatedDir/RDFile1.txt", 10);
        AddFile("RepeatedDir/RepeatedDir/RDFile2.txt", 10);
        AddFile("SpecialDir/SDEnglish.con", 10);
        AddFile("SpecialDir/SDEspañol", 10);
        AddFile("SpecialDir/SDРусский.txt", 10);
        AddFile($"SpecialDir/{SpecialFolder}/SFEnglish.con", 10);
        AddFile($"SpecialDir/{SpecialFolder}/SFEspañol", 10);
        AddFile($"SpecialDir/{SpecialFolder}/SFРусский.txt", 10);
        AddFile("Media/Holiday 2020.mp4", 5 * Mb);
        AddFile("Media/Episode 10.mkv", 300 * Kb);
        AddFile("Media/Episode 2.mkv", 200 * Kb);
        AddFile("Media/photo.jpg", 3 * Kb);
    }

    public string BasePath { get; }
    public string Root { get; }
    public string DatabasePath { get; }
    public string ThumbnailPath { get; }
    public string SettingsPath => Path.Combine(BasePath, "settings.json");
    public string LogsPath => Path.Combine(BasePath, "logs");
    public string ConnectionString => $"Data Source={DatabasePath}";

    /// <summary>Folders under (and including) <see cref="Root"/>.</summary>
    public int DirectoryCount => Directory.GetDirectories(Root, "*", SearchOption.AllDirectories).Length + 1;

    public string FullPath(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    public string AddFile(string relative, long size)
    {
        var path = FullPath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var stream = File.Create(path))
        {
            // Some real content so fingerprints differ, then grow to the requested size.
            var header = System.Text.Encoding.UTF8.GetBytes(relative.PadRight(128, '.'));
            stream.Write(header, 0, (int)Math.Min(header.Length, size));
            stream.SetLength(size);
        }

        return path;
    }

    public Dictionary<string, string?> Settings() => new()
    {
        ["ConnectionStrings:Sfe"] = ConnectionString,
        ["Sfe:Root"] = Root,
        ["Sfe:DataServer"] = "",
        ["Sfe:Database:Provider"] = "Sqlite",
        ["Sfe:Thumbnails:CacheDirectory"] = ThumbnailPath,
        ["Sfe:System:EnableShutdown"] = "false",
        ["Sfe:System:DismountCommand"] = "",
        ["Sfe:SettingsFile"] = SettingsPath,
        ["Sfe:Setup:Code"] = SetupCode,
        // The test clients aren't on the server's own computer (no remote address): a Local server would refuse them.
        // Tests of the Local mode remove it, or set Local.
        ["Sfe:Mode"] = "External",
        // Never next to the app (src/Sfe.Api/data): a test without a code makes one, the installation page writes.
        ["Sfe:Setup:CodeFile"] = Path.Combine(BasePath, "setup-code.txt"),
        ["Sfe:InstallationFile"] = Path.Combine(BasePath, "installation.json"),
        ["Sfe:Security:KeysDirectory"] = Path.Combine(BasePath, "keys"),
        // The daily log files, when a test turns them on (Sfe:Logs:WriteFiles).
        ["Sfe:Logs:Directory"] = LogsPath,
    };

    public void Dispose()
    {
        // Only this library's pool: ClearAllPools would dispose handles that parallel tests are opening.
        // The app may normalise the connection string, so clear both forms.
        SqliteConnection.ClearPool(new SqliteConnection(ConnectionString));
        SqliteConnection.ClearPool(new SqliteConnection(SfeDatabase.PrepareSqlite(ConnectionString, null)));
        try
        {
            Directory.Delete(BasePath, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: the OS temp cleanup will get it.
        }
    }
}
