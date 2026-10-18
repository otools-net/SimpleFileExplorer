using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Sfe.Core.Data;

namespace Sfe.Core.Logs;

/// <summary>
/// Collects what every logger of the server writes, at the levels of the <c>Logging</c> section (as the console), for
/// the server's log: <see cref="AuditLogService"/> drains the queue into the database and the daily files. One
/// instance for the whole server; each library's services log through <see cref="ForTenant"/>, which marks their
/// entries with the library. Logging only queues (no I/O): at most <see cref="MaxQueued"/> entries wait, the oldest
/// are dropped (and counted) beyond.
/// </summary>
public sealed class AuditLogProvider(TimeProvider time) : ILoggerProvider
{
    public const int MaxQueued = 20_000;
    public const int MaxMessageLength = 8_000;
    public const int MaxExceptionLength = 16_000;

    /// <summary>Set while the log itself works (its database commands, its own failures): those are not logged again.</summary>
    private static readonly AsyncLocal<bool> SuppressedFlow = new();

    private readonly ConcurrentQueue<LogEntry> _queue = new();
    private int _count;
    private long _dropped;

    public AuditLogProvider() : this(TimeProvider.System)
    {
    }

    public ILogger CreateLogger(string categoryName) => new AuditLogger(this, categoryName, null);

    /// <summary>The output of a library's services: the same queue, its entries marked with <paramref name="tenantId"/>.</summary>
    public ILoggerProvider ForTenant(int tenantId) => new TenantOutput(this, tenantId);

    /// <summary>Until disposed, what the calling flow logs is left out of the log (the console still shows it).</summary>
    public static IDisposable Suppress()
    {
        var previous = SuppressedFlow.Value;
        SuppressedFlow.Value = true;
        return new Restore(previous);
    }

    /// <summary>Entries waiting to be written.</summary>
    public int Queued => Volatile.Read(ref _count);

    /// <summary>Takes up to <paramref name="max"/> waiting entries, oldest first.</summary>
    internal List<LogEntry> Drain(int max)
    {
        var entries = new List<LogEntry>(Math.Min(max, Math.Max(Queued, 1)));
        while (entries.Count < max && _queue.TryDequeue(out var entry))
        {
            Interlocked.Decrement(ref _count);
            entries.Add(entry);
        }

        return entries;
    }

    /// <summary>The entries dropped since the last call, the queue being full.</summary>
    internal long TakeDropped() => Interlocked.Exchange(ref _dropped, 0);

    internal void Add(string category, int? tenantId, LogEntryLevel level, string message, Exception? exception)
    {
        _queue.Enqueue(new LogEntry
        {
            Timestamp = time.GetUtcNow().UtcDateTime,
            Level = level,
            Category = Truncate(category, AccessDbContext.MaxLogCategoryLength),
            Message = Truncate(message, MaxMessageLength),
            Exception = exception is null ? null : Truncate(exception.ToString(), MaxExceptionLength),
            TenantId = tenantId,
        });
        if (Interlocked.Increment(ref _count) <= MaxQueued)
            return;
        if (_queue.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _count);
            Interlocked.Increment(ref _dropped);
        }
    }

    public void Dispose()
    {
    }

    private static string Truncate(string text, int length) => text.Length > length ? text[..(length - 1)] + "…" : text;

    private sealed class AuditLogger(AuditLogProvider provider, string category, int? tenantId) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && !SuppressedFlow.Value;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;
            provider.Add(category, tenantId, SecurityLog.LevelOf(logLevel, eventId), formatter(state, exception), exception);
        }
    }

    /// <summary>Handed to a library's services as an instance: their container doesn't dispose the server's queue.</summary>
    private sealed class TenantOutput(AuditLogProvider provider, int tenantId) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new AuditLogger(provider, categoryName, tenantId);

        public void Dispose()
        {
        }
    }

    private sealed class Restore(bool previous) : IDisposable
    {
        public void Dispose() => SuppressedFlow.Value = previous;
    }
}
