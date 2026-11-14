namespace Sfe.Tests.Infrastructure;

/// <summary>
/// A clock whose waits end at once: a timer (what <c>Task.Delay(time, clock)</c> makes) moves the clock by its time,
/// is noted in <see cref="Waits"/> and fires. Tests of retries and backoffs run without waiting.
/// </summary>
public sealed class InstantClock : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<TimeSpan> _waits = [];
    private DateTimeOffset _now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The waits asked, in order.</summary>
    public IReadOnlyList<TimeSpan> Waits
    {
        get
        {
            lock (_gate)
                return [.. _waits];
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
            return _now;
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (_gate)
        {
            _waits.Add(dueTime);
            _now += dueTime;
        }
        ThreadPool.QueueUserWorkItem(_ => callback(state));
        return new FiredTimer();
    }

    private sealed class FiredTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => false;

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
