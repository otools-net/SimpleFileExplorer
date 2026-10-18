using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sfe.Core.Data;
using Sfe.Core.Options;
using Sfe.Core.Settings;

namespace Sfe.Core.Logs;

/// <summary>A search of the log. Every filter is optional.</summary>
/// <param name="Text">Found in the message, the logger's name or the exception (ignoring case).</param>
/// <param name="Levels">Only these levels.</param>
/// <param name="From">From this moment on.</param>
/// <param name="To">Before this moment.</param>
/// <param name="Tenant">Only the entries of this library; 0 = only the server's own (no library).</param>
/// <param name="Before">The <see cref="LogPage.Next"/> of the previous page.</param>
public sealed record LogQuery(
    string? Text = null, IReadOnlyCollection<LogEntryLevel>? Levels = null, DateTimeOffset? From = null, DateTimeOffset? To = null,
    int? Tenant = null, long? Before = null, int? Limit = null);

/// <param name="TenantId">The library whose services logged it; null for the server's own.</param>
public sealed record LogEntryView(long Id, DateTimeOffset Timestamp, LogEntryLevel Level, string Category, string Message, string? Exception, int? TenantId);

/// <param name="Entries">Newest first.</param>
/// <param name="Next">The <c>before</c> of the next (older) page; null on the last one.</param>
public sealed record LogPage(IReadOnlyList<LogEntryView> Entries, long? Next);

/// <summary>
/// The server's log: what <see cref="AuditLogProvider"/> collects, written in batches to the accounts' database
/// (<see cref="AccessDbContext.LogEntries"/>) and, when <see cref="LogOptions.WriteFiles"/>, appended to a file per day
/// in <see cref="Directory"/>; searched newest first by key (no offset), and forgotten after
/// <see cref="LogOptions.RetentionDays"/>. A worker of the Api calls <see cref="FlushAsync"/> and
/// <see cref="PurgeAsync"/>: before an update is confirmed nothing is written to the database, entries wait in memory
/// (at most <see cref="AuditLogProvider.MaxQueued"/>) while the files still get them.
/// </summary>
public sealed class AuditLogService(
    AuditLogProvider queue, IDbContextFactory<AccessDbContext> dbFactory, SettingLayers settings, string directory, ILogger<AuditLogService> logger)
{
    public const int DefaultPageSize = 100;
    public const int MaxPageSize = 500;
    public const int MaxTextLength = 200;
    private const int BatchSize = 1000;
    private const int PurgeBatchSize = 5000;
    private const string FilePrefix = "sfe-";
    private const string FileExtension = ".log";

    private readonly SemaphoreSlim _gate = new(1, 1);
    /// <summary>Drained, not in the database yet (it couldn't be written, or the update isn't confirmed).</summary>
    private readonly List<LogEntry> _pending = [];
    private bool _databaseFailing;
    private bool _filesFailing;

    /// <summary>The folder of the daily files (full path).</summary>
    public string Directory { get; } = directory;

    /// <summary>The log's settings in effect: the configuration's, then the server's settings page (server-level keys).</summary>
    public LogOptions Options() => settings.ServerLevel().Logs;

    /// <summary>
    /// Writes what was logged since the last call: to the day's file when files are on, and to the database when
    /// <paramref name="database"/> (false until the update of the install is confirmed).
    /// </summary>
    public async Task FlushAsync(bool database = true, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        // The database commands of the log, and its own failures, are not logged into it again.
        using var suppressed = AuditLogProvider.Suppress();
        try
        {
            var options = Options();
            while (true)
            {
                var batch = queue.Drain(BatchSize);
                if (queue.TakeDropped() is > 0 and var dropped)
                    batch.Insert(0, Dropped(dropped));
                if (batch.Count == 0)
                    break;
                if (options.WriteFiles)
                    await WriteFilesAsync(batch, cancellationToken);
                _pending.AddRange(batch);
            }

            if (_pending.Count > AuditLogProvider.MaxQueued)
                _pending.RemoveRange(0, _pending.Count - AuditLogProvider.MaxQueued);
            if (database && _pending.Count > 0)
                await WriteDatabaseAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>A page of entries, newest first.</summary>
    public async Task<LogPage> SearchAsync(LogQuery query, CancellationToken cancellationToken = default)
    {
        var size = Math.Clamp(query.Limit ?? DefaultPageSize, 1, MaxPageSize);
        using var suppressed = AuditLogProvider.Suppress();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entries = db.LogEntries.AsNoTracking();
        if (query.Tenant is 0)
            entries = entries.Where(e => e.TenantId == null);
        else if (query.Tenant is { } tenant)
            entries = entries.Where(e => e.TenantId == tenant);
        if (query.Levels is { Count: > 0 } levels)
        {
            var wanted = levels.Distinct().ToList();
            entries = entries.Where(e => wanted.Contains(e.Level));
        }
        if (query.From is { } from)
        {
            var start = from.UtcDateTime;
            entries = entries.Where(e => e.Timestamp >= start);
        }
        if (query.To is { } to)
        {
            var end = to.UtcDateTime;
            entries = entries.Where(e => e.Timestamp < end);
        }
        if (query.Text?.Trim() is { Length: > 0 } text)
        {
            var pattern = "%" + EscapeLike(text.Length > MaxTextLength ? text[..MaxTextLength] : text) + "%";
            entries = entries.Where(e => EF.Functions.Like(e.Message, pattern, LikeEscape)
                || EF.Functions.Like(e.Category, pattern, LikeEscape)
                || (e.Exception != null && EF.Functions.Like(e.Exception, pattern, LikeEscape)));
        }
        if (query.Before is { } before)
            entries = entries.Where(e => e.Id < before);

        var rows = await entries.OrderByDescending(e => e.Id).Take(size + 1).ToListAsync(cancellationToken);
        var page = rows.Take(size)
            .Select(e => new LogEntryView(e.Id, new DateTimeOffset(DateTime.SpecifyKind(e.Timestamp, DateTimeKind.Utc)), e.Level, e.Category, e.Message, e.Exception, e.TenantId))
            .ToList();
        return new LogPage(page, rows.Count > size ? page[^1].Id : null);
    }

    /// <summary>Forgets the entries and the daily files older than <see cref="LogOptions.RetentionDays"/> (0: never).</summary>
    public async Task PurgeAsync(CancellationToken cancellationToken = default)
    {
        using var suppressed = AuditLogProvider.Suppress();
        var days = Options().RetentionDays;
        if (days <= 0)
            return;
        var limit = DateTime.UtcNow.AddDays(-days);
        var deleted = 0;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            // In slices, so that a large backlog (a first purge after raising the level) doesn't hold one long transaction.
            while (true)
            {
                var last = await db.LogEntries.Where(e => e.Timestamp < limit).OrderBy(e => e.Id).Skip(PurgeBatchSize - 1)
                    .Select(e => (long?)e.Id).FirstOrDefaultAsync(cancellationToken);
                var count = last is { } bound
                    ? await db.LogEntries.Where(e => e.Id <= bound && e.Timestamp < limit).ExecuteDeleteAsync(cancellationToken)
                    : await db.LogEntries.Where(e => e.Timestamp < limit).ExecuteDeleteAsync(cancellationToken);
                deleted += count;
                if (last is null || count == 0)
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cannot forget the old entries of the log");
        }

        var files = PurgeFiles(DateOnly.FromDateTime(limit));
        if (deleted > 0 || files > 0)
            logger.LogInformation("Forgot {Count} log entries and {Files} log files older than {Days} days", deleted, files, days);
    }

    /// <summary>The name of the daily file of <paramref name="day"/> (UTC).</summary>
    public static string FileName(DateOnly day) => FilePrefix + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + FileExtension;

    /// <summary>A line of a daily file; the lines of a multi-line message or exception follow, indented.</summary>
    public static string FormatLine(LogEntry entry)
    {
        var line = new StringBuilder()
            .Append(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture))
            .Append(' ').Append(ShortLevel(entry.Level))
            .Append(entry.TenantId is { } tenant ? $" [space {tenant}]" : "")
            .Append(' ').Append(entry.Category).Append(": ")
            .Append(Indent(entry.Message));
        if (entry.Exception is { } exception)
            line.Append("\n    ").Append(Indent(exception));
        return line.Append('\n').ToString();
    }

    private async Task WriteDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (_pending.Count > 0)
            {
                var batch = _pending.Take(BatchSize).ToList();
                await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
                // New objects each time: a failed save leaves its entities tracked with ids.
                db.LogEntries.AddRange(batch.Select(Copy));
                await db.SaveChangesAsync(cancellationToken);
                _pending.RemoveRange(0, batch.Count);
            }

            if (_databaseFailing)
                logger.LogInformation("The log is written to the database again");
            _databaseFailing = false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Kept for the next try; said once (on the console: the log can't hold it).
            if (!_databaseFailing)
                logger.LogWarning(ex, "Cannot write the log to the database; {Count} entries wait in memory", _pending.Count);
            _databaseFailing = true;
        }
    }

    private async Task WriteFilesAsync(List<LogEntry> batch, CancellationToken cancellationToken)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            foreach (var day in batch.GroupBy(e => DateOnly.FromDateTime(e.Timestamp)))
            {
                var text = string.Concat(day.Select(FormatLine));
                await File.AppendAllTextAsync(Path.Combine(Directory, FileName(day.Key)), text, Encoding.UTF8, cancellationToken);
            }

            _filesFailing = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!_filesFailing)
                logger.LogWarning(ex, "Cannot write the log files in {Directory}", Directory);
            _filesFailing = true;
        }
    }

    private int PurgeFiles(DateOnly before)
    {
        if (!System.IO.Directory.Exists(Directory))
            return 0;
        var deleted = 0;
        try
        {
            foreach (var path in System.IO.Directory.EnumerateFiles(Directory, FilePrefix + "*" + FileExtension))
            {
                var name = Path.GetFileNameWithoutExtension(path)[FilePrefix.Length..];
                if (!DateOnly.TryParseExact(name, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) || day >= before)
                    continue;
                File.Delete(path);
                deleted++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Cannot delete the old log files in {Directory}", Directory);
        }

        return deleted;
    }

    private static LogEntry Dropped(long count) => new()
    {
        Timestamp = DateTime.UtcNow,
        Level = LogEntryLevel.Warning,
        Category = typeof(AuditLogService).FullName!,
        Message = $"{count} log entries were left out: more were logged than the log could keep up with.",
    };

    private static LogEntry Copy(LogEntry e) => new()
    {
        Timestamp = e.Timestamp, Level = e.Level, Category = e.Category, Message = e.Message, Exception = e.Exception, TenantId = e.TenantId,
    };

    private const string LikeEscape = "\\";

    private static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");

    private static string ShortLevel(LogEntryLevel level) => level switch
    {
        LogEntryLevel.Trace => "TRC",
        LogEntryLevel.Debug => "DBG",
        LogEntryLevel.Information => "INF",
        LogEntryLevel.Warning => "WRN",
        LogEntryLevel.SecurityWarning => "SEC",
        LogEntryLevel.Error => "ERR",
        LogEntryLevel.Critical => "CRT",
        _ => level.ToString().ToUpperInvariant(),
    };

    private static string Indent(string text) => text.ReplaceLineEndings("\n    ");
}
