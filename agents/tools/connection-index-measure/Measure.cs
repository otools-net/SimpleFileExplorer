#:package Microsoft.EntityFrameworkCore.Sqlite@10.0.12
#:package Microsoft.EntityFrameworkCore.SqlServer@10.0.12
#:property PublishAot=false
#:property Nullable=enable
// Measures the index of connection files planned in agents/docs/development/connections-index-plan.md (phase 0):
// a throwaway RemoteFiles / RemoteFileTags table pair filled with synthetic rows in batches (as a first sync would),
// then the search's typical queries through predicates written once for any row type (ISearchableFile<T>), on SQLite
// and SQL Server LocalDB. Temp databases only, dropped at the end. See README.md.
//
//   dotnet run agents/tools/connection-index-measure/Measure.cs -- [--rows 3000000] [--provider sqlite|sqlserver|both]
//                                                                  [--batch 1000] [--writer ef|ado] [--keep]
using System.Diagnostics;
using System.Globalization;
using System.Linq.Expressions;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

var rows = IntArg("--rows", 3_000_000);
var batch = IntArg("--batch", 1000);
var provider = StrArg("--provider", "both");
var keep = args.Contains("--keep");
Run.Writer = StrArg("--writer", "ef");
var work = Path.Combine(Path.GetTempPath(), "sfe-connection-index-measure");
Directory.CreateDirectory(work);
Console.WriteLine($"rows {rows:N0}, batch {batch}, writer {Run.Writer}, work folder {work}");

if (provider is "sqlite" or "both")
{
    var file = Path.Combine(work, $"measure-{Guid.NewGuid():N}.db");
    await Run.MeasureAsync("SQLite", $"Data Source={file}", false, rows, batch, work, () => new FileInfo(file).Length);
    SqliteConnection.ClearAllPools();
    if (!keep)
        foreach (var f in Directory.GetFiles(work, Path.GetFileName(file) + "*"))
            File.Delete(f);
}
if (provider is "sqlserver" or "both")
{
    var name = $"sfe_measure_{Guid.NewGuid():N}"[..24];
    var cs = $@"Server=(localdb)\MSSQLLocalDB;Database={name};Integrated Security=true;TrustServerCertificate=true";
    try
    {
        await Run.MeasureAsync("SQL Server LocalDB", cs, true, rows, batch, work, null);
    }
    finally
    {
        if (!keep)
        {
            SqlConnection.ClearAllPools();
            await using var db = Run.Context(cs, true);
            await db.Database.EnsureDeletedAsync();
            Console.WriteLine($"dropped {name}");
        }
    }
}

int IntArg(string name, int fallback) =>
    Array.IndexOf(args, name) is var i and >= 0 ? int.Parse(args[i + 1], CultureInfo.InvariantCulture) : fallback;
string StrArg(string name, string fallback) => Array.IndexOf(args, name) is var i and >= 0 ? args[i + 1] : fallback;

/// <summary>What the search reads, by the same property names on local and remote rows.</summary>
public interface ISearchableFile<TSelf>
    where TSelf : class, ISearchableFile<TSelf>
{
    string FullName { get; }
    string Name { get; }
    string Extension { get; }
    long Size { get; }
    DateTime LastWriteTime { get; }
    int? Width { get; }
    int? Height { get; }
    double? Duration { get; }
    long? Pixels { get; }
    static abstract Expression<Func<TSelf, bool>> HasAnyTag(int[] tagIds);
}

public sealed class RemoteFile : ISearchableFile<RemoteFile>
{
    public long Id { get; set; }
    public int ConnectionId { get; set; }
    public string ProviderId { get; set; } = "";
    public long DirectoryId { get; set; }

    /// <summary>Lowered path: the key, compared and sorted by.</summary>
    public string FullName { get; set; } = "";
    public string DisplayPath { get; set; } = "";
    public string Name { get; set; } = "";
    public string Extension { get; set; } = "";
    public long Size { get; set; }
    public DateTime LastWriteTime { get; set; }
    public string? ContentHash { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? Duration { get; set; }
    public long? Pixels { get; private set; }
    public List<RemoteFileTag> TagLinks { get; set; } = [];

    public static Expression<Func<RemoteFile, bool>> HasAnyTag(int[] tagIds) =>
        f => f.TagLinks.Any(l => tagIds.Contains(l.TagId));
}

public sealed class RemoteFileTag
{
    public long FileId { get; set; }
    public RemoteFile? File { get; set; }
    public int TagId { get; set; }
}

public sealed class MeasureDb(DbContextOptions<MeasureDb> options, bool sqlServer) : DbContext(options)
{
    public DbSet<RemoteFile> RemoteFiles => Set<RemoteFile>();
    public DbSet<RemoteFileTag> RemoteFileTags => Set<RemoteFileTag>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<RemoteFile>(e =>
        {
            e.Property(f => f.FullName).HasMaxLength(260).IsRequired();
            e.Property(f => f.DisplayPath).HasMaxLength(260).IsRequired();
            e.Property(f => f.Name).HasMaxLength(260).IsRequired();
            e.Property(f => f.Extension).HasMaxLength(260);
            e.Property(f => f.ProviderId).HasMaxLength(64).IsUnicode(false);
            e.Property(f => f.ContentHash).HasMaxLength(64).IsUnicode(false);
            e.Property(f => f.Pixels)
                .HasComputedColumnSql(sqlServer ? "CAST([Width] AS bigint) * [Height]" : "\"Width\" * \"Height\"", false);
            if (!sqlServer)
            {
                e.Property(f => f.Name).UseCollation("NOCASE");
                e.Property(f => f.Extension).UseCollation("NOCASE");
            }
            // Every index leads with the connection: a query never reads another connection's rows.
            e.HasIndex(f => new { f.ConnectionId, f.FullName }).IsUnique();
            e.HasIndex(f => new { f.ConnectionId, f.ProviderId }).IsUnique();
            e.HasIndex(f => new { f.ConnectionId, f.DirectoryId });
            e.HasIndex(f => new { f.ConnectionId, f.Name });
            e.HasIndex(f => new { f.ConnectionId, f.Extension });
            e.HasIndex(f => new { f.ConnectionId, f.Size });
            e.HasIndex(f => new { f.ConnectionId, f.LastWriteTime });
            e.HasIndex(f => new { f.ConnectionId, f.Duration });
            e.HasIndex(f => new { f.ConnectionId, f.Pixels });
        });
        b.Entity<RemoteFileTag>(e =>
        {
            e.HasKey(l => new { l.FileId, l.TagId });
            e.HasIndex(l => l.TagId);
            e.HasOne(l => l.File).WithMany(f => f.TagLinks).HasForeignKey(l => l.FileId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

/// <summary>The search's predicates, written once over any <see cref="ISearchableFile{TSelf}"/>.</summary>
public static class Predicates
{
    public static Expression<Func<T, bool>> NameContains<T>(string term)
        where T : class, ISearchableFile<T>
    {
        var pattern = "%" + term + "%";
        return f => EF.Functions.Like(f.Name, pattern, "\\");
    }

    public static Expression<Func<T, bool>> Extension<T>(string ext)
        where T : class, ISearchableFile<T> => f => EF.Functions.Like(f.Extension, ext, "\\");

    public static Expression<Func<T, bool>> SizeOver<T>(long bytes)
        where T : class, ISearchableFile<T> => f => f.Size > bytes;

    public static Expression<Func<T, bool>> Tag<T>(int[] ids)
        where T : class, ISearchableFile<T> => T.HasAnyTag(ids);

    public static Expression<Func<T, bool>> InTree<T>(string folder)
        where T : class, ISearchableFile<T>
    {
        var prefix = folder + "/";
        var pattern = prefix + "%";
        var length = prefix.Length;
        return f => EF.Functions.Like(f.FullName, pattern, "\\") && f.FullName.Substring(0, length) == prefix;
    }

    public static Expression<Func<T, bool>> AfterName<T>(string name, string path)
        where T : class, ISearchableFile<T> =>
        f => string.Compare(f.Name, name) > 0 || f.Name == name && string.Compare(f.FullName, path) > 0;

    public static Expression<Func<T, bool>> Or<T>(Expression<Func<T, bool>> l, Expression<Func<T, bool>> r) =>
        Expression.Lambda<Func<T, bool>>(
            Expression.OrElse(l.Body, new Swap(r.Parameters[0], l.Parameters[0]).Visit(r.Body)),
            l.Parameters
        );

    private sealed class Swap(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
    }
}

public static class Run
{
    private static readonly string[] Words =
    [
        "beach", "sunset", "family", "party", "holiday", "garden", "city", "night", "snow", "lake", "forest", "dog",
        "cat", "birthday", "wedding", "concert", "trip", "school", "work", "scan", "invoice", "report", "draft",
        "final", "mountain", "river", "car", "train", "museum", "food", "market", "bridge", "portrait", "street",
        "harbor", "island", "desert", "castle", "church", "festival", "game", "match", "rain", "spring", "summer",
        "autumn", "winter", "morning", "evening", "kids", "friends", "home", "office", "project", "notes", "music",
        "live", "studio", "demo", "mix", "cover", "clip", "screen", "record",
    ];

    private static readonly (string Ext, int Weight, string Top)[] Types =
    [
        (".jpg", 40, "Photos"), (".png", 5, "Photos"), (".heic", 5, "Photos"), (".mp4", 15, "Videos"),
        (".mov", 5, "Videos"), (".mkv", 2, "Videos"), (".mp3", 8, "Music"), (".flac", 2, "Music"),
        (".pdf", 6, "Documents"), (".docx", 4, "Documents"), (".txt", 4, "Documents"), (".zip", 2, "Archive"),
        ("", 2, "Archive"),
    ];

    public static readonly string[] VideoExtensions = [".mp4", ".mov", ".mkv", ".avi", ".m4v", ".webm", ".wmv"];

    public static MeasureDb Context(string cs, bool sqlServer)
    {
        var o = new DbContextOptionsBuilder<MeasureDb>();
        if (sqlServer)
            o.UseSqlServer(cs);
        else
            o.UseSqlite(cs);
        o.ConfigureWarnings(w => w.Ignore(CoreEventId.RowLimitingOperationWithoutOrderByWarning));
        return new MeasureDb(o.Options, sqlServer);
    }

    public static async Task MeasureAsync(
        string label, string cs, bool sqlServer, int rows, int batch, string work, Func<long>? size)
    {
        Console.WriteLine($"\n## {label}");
        await using (var db = Context(cs, sqlServer))
        {
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
            if (!sqlServer)
                await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;");
        }
        if (Writer == "ado")
        {
            await FillAsync(cs, sqlServer, rows, batch, sqlServer ? BulkCopyAsync(cs) : PreparedInsertAsync(cs));
            await TagPassAsync(cs, sqlServer);
        }
        else
            await FillAsync(cs, sqlServer, rows, batch, EfWriter(cs, sqlServer));
        if (size is not null)
            Console.WriteLine($"file size {size() / 1048576.0:N0} MB");
        else
        {
            await using var db = Context(cs, sqlServer);
            var mb = await db.Database.SqlQueryRaw<long>(
                "SELECT CAST(SUM(used_pages) * 8 / 1024 AS bigint) AS Value FROM sys.allocation_units").SingleAsync();
            Console.WriteLine($"used space {mb:N0} MB");
        }
        await QueriesAsync(cs, sqlServer, label, work);
    }

    /// <summary>"ef" (default: Add, SaveChanges, ChangeTracker.Clear per batch, as DirectoryIndexer) or "ado"
    /// (SQLite: one prepared INSERT reused in a transaction per batch; SQL Server: SqlBulkCopy per batch).</summary>
    public static string Writer = "ef";

    private static (Func<List<RemoteFile>, Task> Write, Func<ValueTask> Dispose) EfWriter(string cs, bool sqlServer)
    {
        var db = Context(cs, sqlServer);
        return (async files =>
        {
            db.RemoteFiles.AddRange(files);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }, db.DisposeAsync);
    }

    private static (Func<List<RemoteFile>, Task> Write, Func<ValueTask> Dispose) PreparedInsertAsync(string cs)
    {
        var connection = new SqliteConnection(cs);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RemoteFiles (ConnectionId, ProviderId, DirectoryId, FullName, DisplayPath, Name, Extension, Size,
                LastWriteTime, ContentHash, Width, Height, Duration)
            VALUES ($c, $p, $d, $f, $dp, $n, $e, $s, $m, $h, $w, $ht, $du)
            """;
        var names = new[] { "$c", "$p", "$d", "$f", "$dp", "$n", "$e", "$s", "$m", "$h", "$w", "$ht", "$du" };
        var parameters = names.Select(n => command.Parameters.Add(new SqliteParameter { ParameterName = n })).ToArray();
        return (files =>
        {
            using var transaction = connection.BeginTransaction();
            command.Transaction = transaction;
            command.Prepare();
            foreach (var f in files)
            {
                object?[] values = [f.ConnectionId, f.ProviderId, f.DirectoryId, f.FullName, f.DisplayPath, f.Name,
                    f.Extension, f.Size, f.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
                    f.ContentHash, f.Width, f.Height, f.Duration];
                for (var i = 0; i < values.Length; i++)
                    parameters[i].Value = values[i] ?? DBNull.Value;
                command.ExecuteNonQuery();
            }
            transaction.Commit();
            return Task.CompletedTask;
        }, () => connection.DisposeAsync());
    }

    private static (Func<List<RemoteFile>, Task> Write, Func<ValueTask> Dispose) BulkCopyAsync(string cs)
    {
        var connection = new SqlConnection(cs);
        connection.Open();
        var table = new System.Data.DataTable();
        foreach (var (name, type) in new (string, Type)[]
        {
            ("ConnectionId", typeof(int)), ("ProviderId", typeof(string)), ("DirectoryId", typeof(long)),
            ("FullName", typeof(string)), ("DisplayPath", typeof(string)), ("Name", typeof(string)),
            ("Extension", typeof(string)), ("Size", typeof(long)), ("LastWriteTime", typeof(DateTime)),
            ("ContentHash", typeof(string)), ("Width", typeof(int)), ("Height", typeof(int)), ("Duration", typeof(double)),
        })
            table.Columns.Add(name, type);
        return (async files =>
        {
            table.Clear();
            foreach (var f in files)
                table.Rows.Add(f.ConnectionId, f.ProviderId, f.DirectoryId, f.FullName, f.DisplayPath, f.Name, f.Extension,
                    f.Size, f.LastWriteTime, f.ContentHash, (object?)f.Width ?? DBNull.Value,
                    (object?)f.Height ?? DBNull.Value, (object?)f.Duration ?? DBNull.Value);
            using var copy = new SqlBulkCopy(connection) { DestinationTableName = "RemoteFiles", BatchSize = files.Count };
            foreach (System.Data.DataColumn column in table.Columns)
                copy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            await copy.WriteToServerAsync(table);
        }, () => connection.DisposeAsync());
    }

    /// <summary>The "ado" writer's tags, as MakeFile gives them: 5% of files, a fifth of them "holiday" (1-3).</summary>
    private static async Task TagPassAsync(string cs, bool sqlServer)
    {
        var watch = Stopwatch.StartNew();
        await using var db = Context(cs, sqlServer);
        db.Database.SetCommandTimeout(600);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO RemoteFileTags (FileId, TagId)
            SELECT Id, CASE WHEN Id % 100 = 0 THEN 1 + (Id / 100) % 3 ELSE 10 + Id % 200 END
            FROM RemoteFiles WHERE Id % 20 = 0
            """);
        Console.WriteLine($"tag pass (one INSERT … SELECT) {watch.Elapsed.TotalSeconds:N1} s");
    }

    /// <summary>Batches as a sync would write them, no transaction across batches.</summary>
    private static async Task FillAsync(
        string cs, bool sqlServer, int rows, int batch, (Func<List<RemoteFile>, Task> Write, Func<ValueTask> Dispose) writer)
    {
        var random = new Random(42);
        var total = Stopwatch.StartNew();
        var window = Stopwatch.StartNew();
        var saving = new Stopwatch();
        long maxHeap = 0;
        var report = Math.Max(rows / 10, batch);
        for (var done = 0; done < rows; done += batch)
        {
            var connection = done / batch % 5 == 4 ? 2 : 1;
            var n = Math.Min(batch, rows - done);
            var files = new List<RemoteFile>(n);
            for (var i = 0; i < n; i++)
                files.Add(MakeFile(random, connection, done + i));
            saving.Start();
            await writer.Write(files);
            saving.Stop();
            maxHeap = Math.Max(maxHeap, GC.GetTotalMemory(false));
            if ((done + n) % report == 0 || done + n == rows)
            {
                Console.WriteLine(
                    $"  {done + n,10:N0} rows, last part {report / window.Elapsed.TotalSeconds,8:N0} rows/s, "
                    + $"managed heap {GC.GetTotalMemory(false) / 1048576.0,6:N0} MB, "
                    + $"working set {Process.GetCurrentProcess().WorkingSet64 / 1048576.0,6:N0} MB");
                window.Restart();
            }
        }
        Console.WriteLine(
            $"filled {rows:N0} rows in {total.Elapsed.TotalSeconds:N0} s ({rows / total.Elapsed.TotalSeconds:N0} rows/s), "
            + $"{saving.Elapsed.TotalSeconds:N0} s writing, max managed heap {maxHeap / 1048576.0:N0} MB, peak working set "
            + $"{Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0:N0} MB");
        await writer.Dispose();
    }

    private static RemoteFile MakeFile(Random r, int connection, int index)
    {
        var pick = r.Next(100);
        var type = Types[^1];
        foreach (var t in Types)
        {
            if ((pick -= t.Weight) < 0)
            {
                type = t;
                break;
            }
        }
        var folder = index / 1000;
        var directory = $"/{type.Top}/{2000 + folder % 26}/{Words[folder % Words.Length]} {folder:D5}";
        var name = $"{Words[r.Next(Words.Length)]}_{Words[r.Next(Words.Length)]}_{index:D7}";
        if (Capitalized(r))
            name = char.ToUpperInvariant(name[0]) + name[1..];
        var display = $"{directory}/{name}{type.Ext}";
        var video = VideoExtensions.Contains(type.Ext);
        var known = r.Next(10) == 0 && type.Top is "Photos" or "Videos";
        var size = type.Top switch
        {
            "Videos" => (long)(r.NextDouble() * 4e9) + 10_000_000,
            "Photos" => r.Next(1_000_000, 10_000_000),
            _ => r.Next(1_000, 5_000_000),
        };
        var file = new RemoteFile
        {
            ConnectionId = connection,
            ProviderId = "id:" + Convert.ToBase64String(BitConverter.GetBytes((long)index * 7919 + connection))
                + Convert.ToBase64String(Guid.NewGuid().ToByteArray())[..10],
            DirectoryId = folder,
            FullName = display.ToLowerInvariant(),
            DisplayPath = display,
            Name = name,
            Extension = type.Ext,
            Size = size,
            LastWriteTime = new DateTime(2005, 1, 1).AddSeconds(r.NextDouble() * 21 * 365 * 86400),
            ContentHash = Convert.ToHexString(Guid.NewGuid().ToByteArray()) + Convert.ToHexString(Guid.NewGuid().ToByteArray()),
            Width = known ? 1920 : null,
            Height = known ? 1080 : null,
            Duration = video && r.Next(10) == 0 ? r.NextDouble() * 3600 : null,
        };
        // 5% tagged: 1% "holiday" (tag 1) or its children (2, 3), the rest among 200 others.
        if (r.Next(100) < 5)
            file.TagLinks.Add(new RemoteFileTag { TagId = r.Next(5) == 0 ? 1 + r.Next(3) : 10 + r.Next(200) });
        return file;
    }

    private static bool Capitalized(Random r) => r.Next(2) == 0;

    private sealed record Row(string FullName, string Name, string Extension, long Size, DateTime Modified, List<int> Tags);

    private static async Task QueriesAsync(string cs, bool sqlServer, string label, string work)
    {
        await using var db = Context(cs, sqlServer);
        var c1 = db.RemoteFiles.AsNoTracking().Where(f => f.ConnectionId == 1);
        var inConnection = await c1.CountAsync();
        Console.WriteLine($"connection 1 holds {inConnection:N0} rows\n");
        Console.WriteLine("| Query | page of 100 (ms) | match count, capped at 10 001 (ms) | count |");
        Console.WriteLine("|---|---|---|---|");

        var sunset = c1.Where(Predicates.NameContains<RemoteFile>("sunset"));
        var firstByName = await Page(sunset.OrderBy(f => f.Name).ThenBy(f => f.FullName)).ToListAsync();
        var last = firstByName[^1];
        var video = Run.VideoExtensions.Select(Predicates.Extension<RemoteFile>).Aggregate(Predicates.Or);
        var queries = new (string Name, IQueryable<RemoteFile> Matches, Func<IQueryable<RemoteFile>, IQueryable<RemoteFile>> Order)[]
        {
            ("name contains 'sunset', path sort", sunset, q => q.OrderBy(f => f.FullName)),
            ("name contains 'sunset', name sort, 2nd page (keyset)",
                sunset.Where(Predicates.AfterName<RemoteFile>(last.Name, last.FullName)),
                q => q.OrderBy(f => f.Name).ThenBy(f => f.FullName)),
            ("name contains 'qqzz' (no match), path sort", c1.Where(Predicates.NameContains<RemoteFile>("qqzz")),
                q => q.OrderBy(f => f.FullName)),
            ("type:video s:>3GB, size descending",
                c1.Where(video).Where(Predicates.SizeOver<RemoteFile>(3_000_000_000)),
                q => q.OrderByDescending(f => f.Size).ThenBy(f => f.FullName)),
            ("e:jpg, modified descending", c1.Where(Predicates.Extension<RemoteFile>(".jpg")),
                q => q.OrderByDescending(f => f.LastWriteTime).ThenBy(f => f.FullName)),
            ("t:holiday (3 tag ids), modified descending", c1.Where(Predicates.Tag<RemoteFile>([1, 2, 3])),
                q => q.OrderByDescending(f => f.LastWriteTime).ThenBy(f => f.FullName)),
            ("t:holiday + name contains 'beach', path sort",
                c1.Where(Predicates.Tag<RemoteFile>([1, 2, 3])).Where(Predicates.NameContains<RemoteFile>("beach")),
                q => q.OrderBy(f => f.FullName)),
            // The tag term as phase 4 writes it on remote rows: from the TagId index (Id IN (SELECT FileId …)).
            ("t:holiday as Id IN (SELECT FileId …), modified descending", c1.Where(TaggedIn(db, [1, 2, 3])),
                q => q.OrderByDescending(f => f.LastWriteTime).ThenBy(f => f.FullName)),
            ("t:holiday as IN + name contains 'beach', path sort",
                c1.Where(TaggedIn(db, [1, 2, 3])).Where(Predicates.NameContains<RemoteFile>("beach")),
                q => q.OrderBy(f => f.FullName)),
            ("folder /photos/2014 and below, path sort", c1.Where(Predicates.InTree<RemoteFile>("/photos/2014")),
                q => q.OrderBy(f => f.FullName)),
        };

        var sql = new List<(string Title, string Text)>();
        foreach (var (name, matches, order) in queries)
        {
            var page = Page(order(matches));
            var count = matches.Take(10_001);
            sql.Add((name, page.ToQueryString()));
            sql.Add((name + ": count", count.Select(f => 1).ToQueryString()));
            var pageMs = await Median(() => page.ToListAsync());
            var n = 0;
            var countMs = await Median(async () => n = await count.CountAsync());
            Console.WriteLine($"| {name} | {pageMs:N1} | {countMs:N1} | {n:N0} |");
        }
        var sqlFile = Path.Combine(work, $"queries-{(sqlServer ? "sqlserver" : "sqlite")}.sql");
        await File.WriteAllLinesAsync(sqlFile, sql.Select(q => $"-- {q.Title}\n{q.Text}\n"));
        Console.WriteLine($"\nSQL of the queries: {sqlFile}");
        if (!sqlServer)
            await PlansAsync(db, sql, sqlFile);
        SameSql(db);
    }

    private static Expression<Func<RemoteFile, bool>> TaggedIn(MeasureDb db, int[] ids)
    {
        var tagged = db.RemoteFileTags.Where(l => ids.Contains(l.TagId)).Select(l => l.FileId);
        return f => tagged.Contains(f.Id);
    }

    private static IQueryable<Row> Page(IQueryable<RemoteFile> ordered) =>
        ordered.Take(101).Select(f => new Row(
            f.FullName, f.Name, f.Extension, f.Size, f.LastWriteTime, f.TagLinks.Select(l => l.TagId).ToList()));

    /// <summary>Median of 5 runs after one warm-up run.</summary>
    private static async Task<double> Median(Func<Task> action)
    {
        await action();
        var times = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            var watch = Stopwatch.StartNew();
            await action();
            times.Add(watch.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        return times[2];
    }

    /// <summary>SQLite's plan of each query, to see which index it reads.</summary>
    private static async Task PlansAsync(MeasureDb db, List<(string Title, string Text)> queries, string sqlFile)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        var plans = new List<string>();
        foreach (var (title, sql) in queries)
        {
            var lines = sql.Split('\n').Where(l => l.Trim().Length > 0).ToList();
            var parameters = lines.Where(l => l.StartsWith(".param set ")).ToList();
            var text = string.Join("\n", lines.Where(l => !l.StartsWith(".param")));
            await using var command = connection.CreateCommand();
            command.CommandText = "EXPLAIN QUERY PLAN " + text;
            foreach (var p in parameters)
            {
                var parts = p[".param set ".Length..].Split(' ', 2);
                var value = parts[1].Trim();
                command.Parameters.AddWithValue(parts[0],
                    value.StartsWith('\'') ? value.Trim('\'').Replace("''", "'") : long.Parse(value, CultureInfo.InvariantCulture));
            }
            plans.Add("-- " + title);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                plans.Add("   " + reader.GetString(3));
        }
        var planFile = Path.ChangeExtension(sqlFile, ".plans.txt");
        await File.WriteAllLinesAsync(planFile, plans);
        Console.WriteLine($"SQLite query plans: {planFile}");
    }

    /// <summary>A predicate written over T gives the same SQL as one written on the entity itself.</summary>
    private static void SameSql(MeasureDb db)
    {
        var generic = db.RemoteFiles.Where(Predicates.SizeOver<RemoteFile>(5)).Where(Predicates.NameContains<RemoteFile>("a"))
            .OrderBy(f => f.FullName).ToQueryString();
        var pattern = "%a%";
        var bytes = 5L;
        var direct = db.RemoteFiles.Where(f => f.Size > bytes).Where(f => EF.Functions.Like(f.Name, pattern, "\\"))
            .OrderBy(f => f.FullName).ToQueryString();
        Console.WriteLine(generic == direct
            ? "generic predicates give the same SQL as predicates on the entity"
            : $"DIFFERENT SQL:\n{generic}\n---\n{direct}");
    }
}
