namespace Otools.Net.Sfe.Core.Tasks;

/// <summary>
/// The runs of the library's background tasks that ended lately, told as they end (<see cref="Ended"/>) and kept in
/// memory (the last <see cref="Kept"/>) for a page that comes back after a while (<see cref="Since"/>): the change
/// feed sends them to the account that started them, and failures to those who see the tasks
/// (agents/docs/features/notifications.md). Nothing is stored: a restart forgets them.
/// </summary>
public sealed class TaskEndings
{
    /// <summary>How many ended runs are kept.</summary>
    public const int Kept = 50;

    private readonly LinkedList<TaskEnding> _recent = new();
    private readonly Lock _gate = new();

    /// <summary>Raised in the flow of the run that ended (handlers only queue it).</summary>
    public event Action<TaskEnding>? Ended;

    /// <summary>The runs kept that ended after <paramref name="since"/>, oldest first.</summary>
    public IReadOnlyList<TaskEnding> Since(DateTimeOffset since)
    {
        lock (_gate)
            return _recent.Where(e => e.Run.Finished > since).ToList();
    }

    internal void Add(TaskEnding ending)
    {
        lock (_gate)
        {
            _recent.AddLast(ending);
            if (_recent.Count > Kept)
                _recent.RemoveFirst();
        }
        Ended?.Invoke(ending);
    }
}
