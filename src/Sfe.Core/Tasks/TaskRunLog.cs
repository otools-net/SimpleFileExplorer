using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Sfe.Core.Tasks;

/// <summary>
/// The log of one run, filled by <see cref="TaskLogProvider"/> with what the task's code logs. A trickplay pass over a
/// large library logs a line per failed video: only the first and the last <see cref="KeptLines"/> lines are kept,
/// the start and the outcome being what a reader looks for.
/// </summary>
public sealed class TaskRunLog
{
    public const int KeptLines = 1000;
    public const int MaxMessageLength = 2000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Lock _gate = new();
    private readonly List<TaskLogLine> _head = [];
    private readonly Queue<TaskLogLine> _tail = new();
    private int _omitted;

    public void Add(LogLevel level, string message)
    {
        if (message.Length > MaxMessageLength)
            message = message[..MaxMessageLength] + "…";
        var line = new TaskLogLine(DateTimeOffset.UtcNow, level.ToString(), message);
        lock (_gate)
        {
            if (_head.Count < KeptLines)
            {
                _head.Add(line);
                return;
            }

            _tail.Enqueue(line);
            if (_tail.Count > KeptLines)
            {
                _tail.Dequeue();
                _omitted++;
            }
        }
    }

    public (List<TaskLogLine> Lines, int Omitted) Snapshot()
    {
        lock (_gate)
            return ([.. _head, .. _tail], _omitted);
    }

    public string Serialize()
    {
        var (lines, omitted) = Snapshot();
        return JsonSerializer.Serialize(new Stored(lines, omitted), Json);
    }

    public static (IReadOnlyList<TaskLogLine> Lines, int Omitted) Deserialize(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return ([], 0);
        try
        {
            var stored = JsonSerializer.Deserialize<Stored>(json, Json);
            return (stored?.Lines ?? [], stored?.Omitted ?? 0);
        }
        catch (JsonException)
        {
            return ([], 0);
        }
    }

    private sealed record Stored(List<TaskLogLine> Lines, int Omitted);
}

/// <summary>
/// Copies the log entries of SFE's own code (categories <c>Sfe.*</c>, from Debug up) into the log of the run in
/// progress on the same async flow: the run sets <see cref="Current"/>, and the tasks and threads it starts inherit
/// it. Entries logged outside a run (requests, the trickplay queue of the player) are ignored.
/// </summary>
public sealed class TaskLogProvider : ILoggerProvider
{
    private static readonly AsyncLocal<TaskRunLog?> CurrentLog = new();

    /// <summary>
    /// Set by <see cref="TaskHistory.Begin"/>. Must be set from the run's own (async) method, not from an async
    /// method it awaits: those restore the caller's value when they return.
    /// </summary>
    internal static TaskRunLog? Current
    {
        get => CurrentLog.Value;
        set => CurrentLog.Value = value;
    }

    public ILogger CreateLogger(string categoryName) => categoryName.StartsWith("Sfe.", StringComparison.Ordinal) ? RunLogger.Instance : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    public void Dispose()
    {
    }

    private sealed class RunLogger : ILogger
    {
        public static readonly RunLogger Instance = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel is >= LogLevel.Debug and < LogLevel.None && Current is not null;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;
            var message = formatter(state, exception);
            if (exception is not null)
                message = $"{message}: {exception.Message}";
            Current?.Add(logLevel, message);
        }
    }
}
