using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Otools.Net.Sfe.Tests.Infrastructure;

/// <summary>
/// Keeps the SQL text of every command EF Core runs (its <c>CommandExecuted</c> log, without the parameters' values):
/// pass <see cref="AddTo"/> to <see cref="CoreServices.CreateAsync"/>, read <see cref="Commands"/>.
/// </summary>
public sealed class CommandLog : ILoggerProvider
{
    private const string Category = "Microsoft.EntityFrameworkCore.Database.Command";

    private readonly ConcurrentQueue<string> _commands = new();

    public void AddTo(ILoggingBuilder builder) =>
        builder.AddProvider(this).AddFilter<CommandLog>(Category, LogLevel.Information);

    /// <summary>The commands run so far, in order.</summary>
    public IReadOnlyCollection<string> Commands => _commands;

    /// <summary>Forgets the commands run so far.</summary>
    public void Clear() => _commands.Clear();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName == Category);

    public void Dispose() { }

    private sealed class Logger(CommandLog log, bool commands) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => commands && logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (!IsEnabled(logLevel) || state is not IEnumerable<KeyValuePair<string, object?>> values)
                return;
            if (values.FirstOrDefault(v => v.Key == "commandText").Value is string text)
                log._commands.Enqueue(text.ReplaceLineEndings("\n"));
        }
    }
}
