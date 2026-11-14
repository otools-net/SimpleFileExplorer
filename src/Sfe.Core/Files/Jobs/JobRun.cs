using System.Collections.Concurrent;

namespace Sfe.Core.Files;

/// <summary>
/// One move, copy, extraction or compression (<see cref="FileJobs"/>), or transfer with a connection
/// (<see cref="Transfers.TransferJobs"/>): its progress and its answers.
/// </summary>
internal sealed class JobRun(
    int id,
    FileJobRequest request,
    FileActor actor,
    IFileJobAccess access,
    CancellationTokenSource cancellation
)
{
    private readonly Lock _gate = new();
    private readonly List<FileJobItem> _results = [];
    private ConflictChoice? _forAll = request.Conflicts == ConflictChoice.Ask ? null : request.Conflicts;
    private TaskCompletionSource<ConflictChoice>? _answer;
    private FileJobConflict? _conflict;
    private long _bytesDone;
    private DateTimeOffset? _finished;

    public int Id { get; } = id;
    public FileJobRequest Request { get; } = request;
    public FileActor Actor { get; } = actor;
    public IFileJobAccess Access { get; } = access;
    public CancellationTokenSource Cancellation { get; } = cancellation;
    public DateTimeOffset Started { get; } = DateTimeOffset.Now;
    public FileJobState State { get; private set; } = FileJobState.Running;
    public long BytesTotal { get; set; }
    public string? Current { get; set; }
    public Task? Completion { get; set; }

    public void AddBytes(long bytes) => Interlocked.Add(ref _bytesDone, bytes);

    /// <summary>How far the job is in its source (an extraction: in the archive's file).</summary>
    public void SetBytes(long bytes) => Interlocked.Exchange(ref _bytesDone, bytes);

    /// <summary>The archive's password, while the job runs; never shown.</summary>
    public string? Password { get; set; }

    /// <summary>The partial folders the job writes into.</summary>
    public ConcurrentBag<string> Partials { get; init; } = [];

    /// <summary>A transfer's connection it reads from; null for the library.</summary>
    public int? FromConnection { get; init; }

    /// <summary>A transfer's connection it writes into; null for the library.</summary>
    public int? ToConnection { get; init; }

    public void Report(FileJobItem item)
    {
        lock (_gate)
            _results.Add(item);
    }

    public async Task<ConflictChoice> ChooseAsync(FileJobConflict conflict, CancellationToken token)
    {
        Task<ConflictChoice> answer;
        lock (_gate)
        {
            if (_forAll is { } choice && (choice != ConflictChoice.Replace || conflict.CanReplace))
                return choice;
            _conflict = conflict;
            _answer = new TaskCompletionSource<ConflictChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
            answer = _answer.Task;
            State = FileJobState.Waiting;
        }
        try
        {
            return await answer.WaitAsync(token);
        }
        finally
        {
            lock (_gate)
            {
                _conflict = null;
                _answer = null;
                if (State == FileJobState.Waiting)
                    State = FileJobState.Running;
            }
        }
    }

    public bool Answer(ConflictChoice choice, bool forAll)
    {
        lock (_gate)
        {
            if (_answer is null)
                return false;
            if (forAll)
                _forAll = choice;
            return _answer.TrySetResult(choice);
        }
    }

    public void Finish(FileJobState state)
    {
        lock (_gate)
        {
            State = state;
            _finished = DateTimeOffset.Now;
            Current = null;
        }
    }

    public FileJobView View()
    {
        lock (_gate)
        {
            return new FileJobView(
                Id,
                Request.Kind,
                State,
                Request.Target,
                Request.Kind == FileJobKind.Compress ? 1 : Request.Paths.Distinct(StringComparer.Ordinal).Count(),
                _results.Count,
                BytesTotal,
                Interlocked.Read(ref _bytesDone),
                Current,
                _conflict,
                [.. _results],
                Started,
                _finished,
                Actor.AccountId,
                FromConnection,
                ToConnection
            );
        }
    }
}
