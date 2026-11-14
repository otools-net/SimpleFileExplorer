using System.Collections.Concurrent;
using Sfe.Core.Paths;

namespace Sfe.Core.Files;

/// <summary>
/// The library's file jobs (agents/docs/features/move-and-copy.md): their ids, the list the operations panel shows,
/// answers to their questions and cancels; finished jobs are kept for the panel, the newest ones only.
/// </summary>
public sealed class JobBoard
{
    /// <summary>Finished jobs kept for the operations panel.</summary>
    private const int KeptJobs = 50;

    private readonly ConcurrentDictionary<int, JobRun> _jobs = new();
    private int _nextId;

    /// <summary>The jobs of <paramref name="ownerId"/> (all with null), newest first.</summary>
    public IReadOnlyList<FileJobView> List(int? ownerId) =>
        [
            .. _jobs
                .Values.Where(j => ownerId is null || j.Actor.AccountId == ownerId)
                .OrderByDescending(j => j.Id)
                .Select(j => j.View()),
        ];

    public FileJobView? Find(int id) => _jobs.TryGetValue(id, out var job) ? job.View() : null;

    /// <summary>The job's task; for tests.</summary>
    internal Task? Completion(int id) => _jobs.TryGetValue(id, out var job) ? job.Completion : null;

    /// <summary>
    /// Answers the job's question about a name clash; <paramref name="forAll"/>: the same for the next ones.
    /// </summary>
    public bool Answer(int id, ConflictChoice choice, bool forAll) =>
        choice != ConflictChoice.Ask && _jobs.TryGetValue(id, out var job) && job.Answer(choice, forAll);

    public bool Cancel(int id)
    {
        if (!_jobs.TryGetValue(id, out var job) || job.State is FileJobState.Completed or FileJobState.Cancelled)
            return false;
        job.Cancellation.Cancel();
        return true;
    }

    /// <summary>The id of the next job.</summary>
    internal int NextId() => Interlocked.Increment(ref _nextId);

    internal void Add(JobRun job) => _jobs[job.Id] = job;

    /// <summary>The partial folders and files the other running or waiting jobs write into.</summary>
    internal HashSet<string> PartialsOfOthers(JobRun job)
    {
        return _jobs
            .Values.Where(j => j != job && j.State is FileJobState.Running or FileJobState.Waiting)
            .SelectMany(j => j.Partials)
            .ToHashSet(LibraryPaths.PathComparer);
    }

    /// <summary>Forgets the finished jobs past the newest <see cref="KeptJobs"/>.</summary>
    internal void Prune()
    {
        foreach (
            var old in _jobs
                .Values.Where(j => j.State is FileJobState.Completed or FileJobState.Cancelled)
                .OrderByDescending(j => j.Id)
                .Skip(KeptJobs)
                .ToList()
        )
        {
            _jobs.TryRemove(old.Id, out _);
        }
    }
}
