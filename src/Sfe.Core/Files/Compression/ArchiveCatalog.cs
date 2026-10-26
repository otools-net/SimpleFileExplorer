using System.Globalization;
using Microsoft.Data.Sqlite;
using Sfe.Core.Options;

namespace Sfe.Core.Files.Compression;

/// <summary>What a catalog holds, and whether its list is whole.</summary>
/// <param name="Complete">False when the read stopped (a limit, a broken archive): the view says why, from
/// <paramref name="Stopped"/>, and never shows the part read as the whole archive.</param>
/// <param name="NeedsPassword">The read stopped at a password (the archive's list is encrypted).</param>
public sealed record ArchiveCatalogSummary(
    ArchiveFormat Format,
    long Entries,
    long Folders,
    long LeftOut,
    bool Complete,
    string? Stopped,
    bool NeedsPassword = false);

/// <param name="Folders">Folders right inside it.</param>
/// <param name="Files">Entries right inside it (files, links…).</param>
/// <param name="AllFiles">Files in its whole tree; <paramref name="Size"/> and <paramref name="Packed"/> are theirs.</param>
public sealed record ArchiveFolder(
    long Id,
    long ParentId,
    string Name,
    int Depth,
    DateTime? Time,
    long Folders,
    long Files,
    long AllFiles,
    long Size,
    long Packed);

/// <summary>An entry of the catalog: where it is (folder, name) and what is needed to open it.</summary>
public sealed record ArchiveFile(long Id, long FolderId, string Name, ArchiveEntry Entry);

/// <summary>A folder's page: its folders first, then its files, by name.</summary>
public sealed record ArchivePage(IReadOnlyList<ArchiveFolder> Folders, IReadOnlyList<ArchiveFile> Files, ArchiveCursor? Next);

/// <summary>Where a page stopped: in the folders or the files, after this name and id (keyset, never an offset).</summary>
public sealed record ArchiveCursor(bool InFiles, string Name, long Id);

/// <summary>The end of the path to a folder: the root is always first, then the <c>Skipped</c> folders between, then <c>Last</c>.</summary>
public sealed record ArchiveTrail(int Skipped, IReadOnlyList<ArchiveFolder> Last);

/// <summary>
/// An archive's list of entries, read once into a SQLite file so that every later question (a folder's page, its
/// totals, the way to it, one entry) is an indexed query: its cost follows the page, never the archive
/// (agents/docs/development/compressed-archives-plan.md, "Scale"). Folders are rows by id with their parent: a path
/// is never stored whole, however deep. Every walk over depth is a loop: a tree 10 000 deep has no recursion to
/// overflow.
/// </summary>
public sealed class ArchiveCatalog(string path)
{
    /// <summary>Changes with the schema: catalogs of another version are made again, never read.</summary>
    public const int Version = 1;

    /// <summary>The root folder's id (its parent is 0, no folder).</summary>
    public const long RootId = 1;

    /// <summary>Entries kept in the left-out list; past it they are only counted.</summary>
    public const int MaxLeftOutListed = 1000;

    private const int BatchRows = 10_000;
    private const int FolderCacheCount = 200_000;

    public string Path { get; } = path;

    /// <summary>
    /// Reads the archive's list into a new catalog at <paramref name="catalogPath"/> (written beside it, then moved
    /// in: a cut build leaves no catalog that looks whole). Memory stays flat: rows go in batches, the folder cache is
    /// bounded, and the archive is read as a stream.
    /// </summary>
    public static ArchiveCatalog Build(
        string archivePath,
        ArchiveFormat format,
        string catalogPath,
        ArchiveOptions options,
        IProgress<long>? progress = null,
        CancellationToken cancel = default)
    {
        var building = catalogPath + ".building";
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(catalogPath)!);
        File.Delete(building);
        try
        {
            using (var connection = Connect(building, SqliteOpenMode.ReadWriteCreate))
            {
                Execute(connection, """
                    PRAGMA journal_mode = OFF;
                    PRAGMA synchronous = OFF;
                    PRAGMA cache_size = -16000;
                    CREATE TABLE Info (Key TEXT PRIMARY KEY, Value TEXT) WITHOUT ROWID;
                    CREATE TABLE Folders (
                        Id INTEGER PRIMARY KEY, ParentId INTEGER NOT NULL, Name TEXT NOT NULL COLLATE NOCASE,
                        Depth INTEGER NOT NULL, Time INTEGER,
                        Folders INTEGER NOT NULL DEFAULT 0, Files INTEGER NOT NULL DEFAULT 0,
                        AllFiles INTEGER NOT NULL DEFAULT 0, Size REAL NOT NULL DEFAULT 0, Packed REAL NOT NULL DEFAULT 0);
                    CREATE INDEX FoldersByExactName ON Folders (ParentId, Name COLLATE BINARY);
                    CREATE TABLE Entries (
                        Id INTEGER PRIMARY KEY, FolderId INTEGER NOT NULL, Name TEXT NOT NULL COLLATE NOCASE,
                        Kind INTEGER NOT NULL, Size INTEGER NOT NULL, Packed INTEGER NOT NULL, Time INTEGER,
                        Encrypted INTEGER NOT NULL, Locator INTEGER NOT NULL, Method INTEGER NOT NULL);
                    CREATE TABLE LeftOut (Name TEXT NOT NULL, Reason TEXT NOT NULL);
                    INSERT INTO Folders (Id, ParentId, Name, Depth) VALUES (1, 0, '', 0);
                    """);
                var writer = new Writer(connection, options.MaxDepth);
                string? stopped = null;
                var needsPassword = false;
                try
                {
                    foreach (var entry in ArchiveReader.ReadEntries(archivePath, format, cancel))
                    {
                        if (writer.Entries >= options.MaxEntries)
                        {
                            stopped = $"The archive holds more than {options.MaxEntries:N0} entries: only the first are listed.";
                            break;
                        }
                        writer.Add(entry);
                        if (writer.Entries % BatchRows == 0)
                        {
                            writer.Commit();
                            progress?.Report(writer.Entries);
                        }
                    }
                }
                catch (ArchiveException error)
                {
                    stopped = error.Message;
                    needsPassword = error.NeedsPassword;
                }
                writer.Commit();
                writer.Finish(format, stopped, needsPassword);
                progress?.Report(writer.Entries);
            }
            File.Move(building, catalogPath, overwrite: true);
            return new ArchiveCatalog(catalogPath);
        }
        catch
        {
            File.Delete(building);
            throw;
        }
    }

    public ArchiveCatalogSummary Summary()
    {
        using var connection = Connect(Path, SqliteOpenMode.ReadOnly);
        var info = new Dictionary<string, string?>();
        using (var reader = Command(connection, "SELECT Key, Value FROM Info").ExecuteReader())
            while (reader.Read())
                info[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
        return new ArchiveCatalogSummary(
            Enum.Parse<ArchiveFormat>(info["format"]!),
            long.Parse(info["entries"]!, CultureInfo.InvariantCulture),
            long.Parse(info["folders"]!, CultureInfo.InvariantCulture),
            long.Parse(info["leftOut"]!, CultureInfo.InvariantCulture),
            info["complete"] == "1",
            info.GetValueOrDefault("stopped"),
            info.GetValueOrDefault("needsPassword") == "1");
    }

    /// <summary>Whether the file at <paramref name="catalogPath"/> is a whole catalog of this <see cref="Version"/>.</summary>
    public static bool IsCurrent(string catalogPath)
    {
        if (!File.Exists(catalogPath))
            return false;
        try
        {
            using var connection = Connect(catalogPath, SqliteOpenMode.ReadOnly);
            return Command(connection, "SELECT Value FROM Info WHERE Key = 'version'").ExecuteScalar() as string
                == Version.ToString(CultureInfo.InvariantCulture);
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    public ArchiveFolder? Folder(long id)
    {
        using var connection = Connect(Path, SqliteOpenMode.ReadOnly);
        return FolderOf(connection, id);
    }

    /// <summary>
    /// The folder at <paramref name="folderPath"/> (<c>/</c>-separated, empty = the root), segment by segment: as
    /// written, else in any case; null when there is none.
    /// </summary>
    public long? FindFolder(string folderPath)
    {
        using var connection = Connect(Path, SqliteOpenMode.ReadOnly);
        using var exact = Command(connection, "SELECT Id FROM Folders WHERE ParentId = $parent AND Name = $name COLLATE BINARY LIMIT 1");
        using var anyCase = Command(connection, "SELECT Id FROM Folders WHERE ParentId = $parent AND Name = $name ORDER BY Id LIMIT 1");
        var id = RootId;
        foreach (var segment in folderPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var found = Scalar(exact, id, segment) ?? Scalar(anyCase, id, segment);
            if (found is null)
                return null;
            id = found.Value;
        }
        return id;

        static long? Scalar(SqliteCommand command, long parent, string name)
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$parent", parent);
            command.Parameters.AddWithValue("$name", name);
            return command.ExecuteScalar() is long value ? value : null;
        }
    }

    /// <summary>
    /// The way to folder <paramref name="id"/>: its <paramref name="last"/> nearest folders (itself included), and how
    /// many sit between them and the root. Costs <paramref name="last"/> lookups, never the depth.
    /// </summary>
    public ArchiveTrail? Trail(long id, int last)
    {
        using var connection = Connect(Path, SqliteOpenMode.ReadOnly);
        var folders = new List<ArchiveFolder>();
        var folder = FolderOf(connection, id);
        if (folder is null)
            return null;
        var depth = folder.Depth;
        while (folder is not null && folder.Id != RootId && folders.Count < last)
        {
            folders.Add(folder);
            folder = FolderOf(connection, folder.ParentId);
        }
        folders.Reverse();
        return new ArchiveTrail(depth - folders.Count, folders);
    }

    /// <summary>The whole path of folder <paramref name="id"/> (<c>/</c>-separated, empty for the root): one lookup per level.</summary>
    public string? PathOf(long id)
    {
        using var connection = Connect(Path, SqliteOpenMode.ReadOnly);
        var names = new List<string>();
        var folder = FolderOf(connection, id);
        if (folder is null)
            return null;
        for (var guard = folder.Depth; folder is not null && folder.Id != RootId && guard >= 0; guard--)
        {
            names.Add(folder.Name);
            folder = FolderOf(connection, folder.ParentId);
        }
        names.Reverse();
        return string.Join('/', names);
    }

    /// <summary>
    /// A page of folder <paramref name="folderId"/>: its folders, then its files, by name (any case), after
    /// <paramref name="after"/>. One indexed range per part: the same cost on the first page as on the ten-thousandth.
    /// </summary>
    public ArchivePage Children(long folderId, ArchiveCursor? after, int limit)
    {
        using var connection = Connect(Path, SqliteOpenMode.ReadOnly);
        var folders = new List<ArchiveFolder>();
        var files = new List<ArchiveFile>();
        if (after is null || !after.InFiles)
        {
            using var command = Command(connection, $"""
                SELECT {FolderColumns} FROM Folders
                WHERE ParentId = $parent AND (Name, Id) > ($name, $id)
                ORDER BY Name, Id LIMIT $limit
                """);
            command.Parameters.AddWithValue("$parent", folderId);
            command.Parameters.AddWithValue("$name", after?.Name ?? "");
            command.Parameters.AddWithValue("$id", after?.Id ?? 0);
            command.Parameters.AddWithValue("$limit", limit + 1);
            using var reader = command.ExecuteReader();
            while (reader.Read())
                folders.Add(ReadFolder(reader));
            if (folders.Count > limit)
            {
                folders.RemoveAt(limit);
                return new ArchivePage(folders, files, new ArchiveCursor(false, folders[^1].Name, folders[^1].Id));
            }
            after = null;
        }

        using (var command = Command(connection, $"""
            SELECT {FileColumns} FROM Entries
            WHERE FolderId = $folder AND (Name, Id) > ($name, $id)
            ORDER BY Name, Id LIMIT $limit
            """))
        {
            command.Parameters.AddWithValue("$folder", folderId);
            command.Parameters.AddWithValue("$name", after?.Name ?? "");
            command.Parameters.AddWithValue("$id", after?.Id ?? 0);
            command.Parameters.AddWithValue("$limit", limit - folders.Count + 1);
            using var reader = command.ExecuteReader();
            while (reader.Read())
                files.Add(ReadFile(reader));
        }
        if (files.Count > limit - folders.Count)
        {
            files.RemoveAt(files.Count - 1);
            // The folders filled the page: the next one starts at the first file.
            var next = files.Count == 0 ? new ArchiveCursor(true, "", 0) : new ArchiveCursor(true, files[^1].Name, files[^1].Id);
            return new ArchivePage(folders, files, next);
        }
        return new ArchivePage(folders, files, null);
    }

    public ArchiveFile? Entry(long id)
    {
        using var connection = Connect(Path, SqliteOpenMode.ReadOnly);
        using var command = Command(connection, $"SELECT {FileColumns} FROM Entries WHERE Id = $id");
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadFile(reader) : null;
    }

    /// <summary>The first entries left out (refused names, too deep), with why.</summary>
    public IReadOnlyList<(string Name, string Reason)> LeftOut()
    {
        using var connection = Connect(Path, SqliteOpenMode.ReadOnly);
        using var reader = Command(connection, "SELECT Name, Reason FROM LeftOut ORDER BY rowid").ExecuteReader();
        var list = new List<(string, string)>();
        while (reader.Read())
            list.Add((reader.GetString(0), reader.GetString(1)));
        return list;
    }

    private const string FolderColumns = "Id, ParentId, Name, Depth, Time, Folders, Files, AllFiles, Size, Packed";
    private const string FileColumns = "Id, FolderId, Name, Kind, Size, Packed, Time, Encrypted, Locator, Method";

    private static ArchiveFolder? FolderOf(SqliteConnection connection, long id)
    {
        using var command = Command(connection, $"SELECT {FolderColumns} FROM Folders WHERE Id = $id");
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadFolder(reader) : null;
    }

    private static ArchiveFolder ReadFolder(SqliteDataReader reader) => new(
        reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetInt32(3), TimeOf(reader, 4),
        reader.GetInt64(5), reader.GetInt64(6), reader.GetInt64(7), ToLong(reader.GetDouble(8)), ToLong(reader.GetDouble(9)));

    private static ArchiveFile ReadFile(SqliteDataReader reader)
    {
        var name = reader.GetString(2);
        var entry = new ArchiveEntry(
            name, (ArchiveEntryKind)reader.GetInt32(3), reader.GetInt64(4), reader.GetInt64(5), TimeOf(reader, 6),
            reader.GetInt64(7) != 0, reader.GetInt64(8), reader.GetInt32(9));
        return new ArchiveFile(reader.GetInt64(0), reader.GetInt64(1), name, entry);
    }

    private static DateTime? TimeOf(SqliteDataReader reader, int column) =>
        reader.IsDBNull(column) ? null : new DateTime(reader.GetInt64(column), DateTimeKind.Unspecified);

    /// <summary>Totals are kept as REAL: sizes a hostile directory claims can't overflow a sum.</summary>
    private static long ToLong(double value) => value >= long.MaxValue ? long.MaxValue : (long)value;

    private static SqliteConnection Connect(string file, SqliteOpenMode mode)
    {
        // Not pooled: a catalog is opened per question, and a pooled handle would keep the file locked for its eviction.
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = mode, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static SqliteCommand Command(SqliteConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = Command(connection, sql);
        command.ExecuteNonQuery();
    }

    /// <summary>Fills a catalog: entries in batches, folders found by a stack of the last path and a bounded cache.</summary>
    private sealed class Writer
    {
        private readonly SqliteConnection connection;
        private readonly int maxDepth;
        private readonly SqliteCommand insertFolder, findFolder, folderTime, insertEntry, insertLeftOut;
        private readonly Dictionary<(long Parent, string Name), long> folders = [];
        // The folders of the last entry's path: archives list entries folder by folder, so most entries reuse it.
        private readonly List<string> stackNames = [];
        private readonly List<long> stackIds = [];
        private SqliteTransaction transaction;

        public long Entries { get; private set; }
        public long LeftOut { get; private set; }

        public Writer(SqliteConnection connection, int maxDepth)
        {
            this.connection = connection;
            this.maxDepth = maxDepth;
            transaction = connection.BeginTransaction();
            insertFolder = Prepare("INSERT INTO Folders (ParentId, Name, Depth) VALUES ($parent, $name, $depth) RETURNING Id",
                "$parent", "$name", "$depth");
            findFolder = Prepare("SELECT Id FROM Folders WHERE ParentId = $parent AND Name = $name COLLATE BINARY LIMIT 1",
                "$parent", "$name");
            folderTime = Prepare("UPDATE Folders SET Time = $time WHERE Id = $id", "$time", "$id");
            insertEntry = Prepare("""
                INSERT INTO Entries (FolderId, Name, Kind, Size, Packed, Time, Encrypted, Locator, Method)
                VALUES ($folder, $name, $kind, $size, $packed, $time, $encrypted, $locator, $method)
                """, "$folder", "$name", "$kind", "$size", "$packed", "$time", "$encrypted", "$locator", "$method");
            insertLeftOut = Prepare("INSERT INTO LeftOut (Name, Reason) VALUES ($name, $reason)", "$name", "$reason");
        }

        public void Add(ArchiveEntry entry)
        {
            Entries++;
            var segments = ArchiveEntryNames.Split(entry.Name, maxDepth, out var reason);
            if (segments is null)
            {
                // "./" and "/" folder entries name the root: nothing to list, nothing left out.
                if (entry.Kind == ArchiveEntryKind.Folder && entry.Name.Trim('/', '\\', '.').Length == 0)
                    return;
                if (LeftOut++ < MaxLeftOutListed)
                    Run(insertLeftOut, entry.Name.Length > 1024 ? entry.Name[..1024] + "…" : entry.Name, reason!);
                return;
            }
            if (entry.Kind == ArchiveEntryKind.Folder)
            {
                var id = ResolveFolder(segments, segments.Length);
                if (entry.Time is { } time)
                    Run(folderTime, time.Ticks, id);
                return;
            }
            var folder = ResolveFolder(segments, segments.Length - 1);
            Run(insertEntry, folder, segments[^1], (int)entry.Kind, Math.Max(entry.Size, 0), Math.Max(entry.Packed, 0),
                entry.Time?.Ticks, entry.Encrypted ? 1 : 0, entry.Locator, entry.Method);
        }

        public void Commit()
        {
            transaction.Commit();
            transaction.Dispose();
            transaction = connection.BeginTransaction();
        }

        /// <summary>Indexes, then the totals bottom-up one depth at a time (each level's sums are final when its parents add them).</summary>
        public void Finish(ArchiveFormat format, string? stopped, bool needsPassword)
        {
            Execute("""
                CREATE INDEX FoldersByParent ON Folders (ParentId, Name, Id);
                CREATE INDEX FoldersByDepth ON Folders (Depth);
                CREATE INDEX EntriesByFolder ON Entries (FolderId, Name, Id);
                UPDATE Folders SET Files = c.Count, AllFiles = c.Files, Size = c.Size, Packed = c.Packed
                FROM (SELECT FolderId, count(*) AS Count, sum(Kind = 0) AS Files,
                        total(CASE WHEN Kind = 0 THEN Size END) AS Size, total(CASE WHEN Kind = 0 THEN Packed END) AS Packed
                      FROM Entries GROUP BY FolderId) AS c
                WHERE Folders.Id = c.FolderId;
                """);
            var deepest = Convert.ToInt32(Scalar("SELECT max(Depth) FROM Folders"), CultureInfo.InvariantCulture);
            using var roll = Prepare("""
                UPDATE Folders SET Folders = Folders.Folders + c.ChildCount, AllFiles = Folders.AllFiles + c.ChildFiles,
                    Size = Folders.Size + c.ChildSize, Packed = Folders.Packed + c.ChildPacked
                FROM (SELECT ParentId, count(*) AS ChildCount, sum(AllFiles) AS ChildFiles, total(Size) AS ChildSize,
                        total(Packed) AS ChildPacked
                      FROM Folders WHERE Depth = $depth GROUP BY ParentId) AS c
                WHERE Folders.Id = c.ParentId
                """, "$depth");
            for (var depth = deepest; depth >= 1; depth--)
                Run(roll, depth);

            using var info = Prepare("INSERT INTO Info (Key, Value) VALUES ($key, $value)", "$key", "$value");
            Run(info, "version", Version.ToString(CultureInfo.InvariantCulture));
            Run(info, "format", format.ToString());
            Run(info, "entries", Entries.ToString(CultureInfo.InvariantCulture));
            Run(info, "folders", Convert.ToString(Scalar("SELECT count(*) - 1 FROM Folders"), CultureInfo.InvariantCulture));
            Run(info, "leftOut", LeftOut.ToString(CultureInfo.InvariantCulture));
            Run(info, "complete", stopped is null ? "1" : "0");
            Run(info, "stopped", stopped);
            Run(info, "needsPassword", needsPassword ? "1" : "0");
            transaction.Commit();
            transaction.Dispose();
            foreach (var command in new[] { insertFolder, findFolder, folderTime, insertEntry, insertLeftOut })
                command.Dispose();
        }

        /// <summary>
        /// The id of the folder made of the first <paramref name="count"/> segments, creating what is missing: the part
        /// shared with the last path comes from the stack, the rest from the cache, the index, or a new row.
        /// </summary>
        private long ResolveFolder(string[] segments, int count)
        {
            var common = 0;
            while (common < count && common < stackNames.Count && string.Equals(segments[common], stackNames[common], StringComparison.Ordinal))
                common++;
            stackNames.RemoveRange(common, stackNames.Count - common);
            stackIds.RemoveRange(common, stackIds.Count - common);
            var id = common == 0 ? RootId : stackIds[common - 1];
            for (var i = common; i < count; i++)
            {
                id = Child(id, segments[i], i + 1);
                stackNames.Add(segments[i]);
                stackIds.Add(id);
            }
            return id;
        }

        private long Child(long parent, string name, int depth)
        {
            if (folders.TryGetValue((parent, name), out var id))
                return id;
            if (Run(findFolder, parent, name) is not long found)
                found = (long)Run(insertFolder, parent, name, depth)!;
            if (folders.Count >= FolderCacheCount)
                folders.Clear();
            folders[(parent, name)] = found;
            return found;
        }

        private SqliteCommand Prepare(string sql, params string[] names)
        {
            var command = Command(connection, sql);
            command.Transaction = transaction;
            foreach (var name in names)
                command.Parameters.Add(new SqliteParameter { ParameterName = name });
            return command;
        }

        private object? Run(SqliteCommand command, params object?[] values)
        {
            command.Transaction = transaction;
            for (var i = 0; i < values.Length; i++)
                command.Parameters[i].Value = values[i] ?? DBNull.Value;
            return command.ExecuteScalar();
        }

        private object? Scalar(string sql)
        {
            using var command = Command(connection, sql);
            command.Transaction = transaction;
            return command.ExecuteScalar();
        }

        private void Execute(string sql)
        {
            using var command = Command(connection, sql);
            command.Transaction = transaction;
            command.ExecuteNonQuery();
        }
    }
}
