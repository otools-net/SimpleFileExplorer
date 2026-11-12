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
