using Microsoft.Extensions.Logging;

namespace Otools.Net.Sfe.Core.Tasks;

/// <summary>
/// Copies the log entries of SFE's own code (categories <c>Otools.Net.Sfe.*</c>, and <c>Sfe.*</c> for the names
/// given by hand, from Debug up) into the log of the run in progress on the same async flow: the run sets
/// <see cref="Current"/>, and the tasks and threads it starts inherit it. Entries logged outside a run (requests,
/// the trickplay queue of the player) are ignored.
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

    public ILogger CreateLogger(string categoryName) =>
        categoryName.StartsWith("Otools.Net.Sfe.", StringComparison.Ordinal)
        || categoryName.StartsWith("Sfe.", StringComparison.Ordinal)
            ? RunLogger.Instance
            : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    public void Dispose() { }

    private sealed class RunLogger : ILogger
    {
        public static readonly RunLogger Instance = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel is >= LogLevel.Debug and < LogLevel.None && Current is not null;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
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
