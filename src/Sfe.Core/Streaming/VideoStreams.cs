using System.Collections.Concurrent;

namespace Sfe.Core.Streaming;

/// <summary>Who watches a converted video: shown to the server's administrators (<see cref="VideoStreams.List"/>).</summary>
/// <param name="Library">Name of the library the video is in.</param>
/// <param name="User">The account's name; null for a visitor without one.</param>
/// <param name="Address">The viewer's network address, when known.</param>
public sealed record StreamViewer(string Library, string? User, string? Address);

/// <summary>A video being converted for a viewer.</summary>
/// <param name="Path">The video, relative to its library's root.</param>
/// <param name="Start">Position in the video (seconds) the conversion started from.</param>
/// <param name="Transcoded">The pictures are encoded again (false: H.264 copied, only repackaged).</param>
/// <param name="Bytes">Sent to the viewer so far.</param>
public sealed record StreamActivity(Guid Id, string Path, StreamViewer Viewer, double Start, bool Transcoded, DateTimeOffset Started, long Bytes);

/// <summary>
/// The videos the server converts at the moment, for every library together: one instance for the whole server, as
/// <see cref="Options.StreamingOptions.MaxStreams"/> counts them all (like <see cref="Thumbnails.FfmpegLimits"/>).
/// </summary>
public sealed class VideoStreams(TimeProvider time)
{
    private readonly ConcurrentDictionary<Guid, Lease> _streams = new();
    private readonly Lock _gate = new();

    public VideoStreams() : this(TimeProvider.System)
    {
    }

    /// <summary>Running now, oldest first.</summary>
    public IReadOnlyList<StreamActivity> List() =>
        [.. _streams.Values.Select(s => s.Snapshot()).OrderBy(s => s.Started)];

    /// <summary>
    /// Registers a stream, unless <paramref name="max"/> are running. A stream of the same <paramref name="client"/> (one
    /// player, which seeks by asking again from another position) is stopped first and doesn't count: the viewer's
    /// browser drops the old request, but not always before sending the new one.
    /// </summary>
    /// <returns>Null when the server is busy; else the stream, to dispose of when it ends.</returns>
    public Lease? TryBegin(string? client, int max, string path, StreamViewer viewer, double start, bool transcoded)
    {
        lock (_gate)
        {
            if (client is not null)
            {
                foreach (var previous in _streams.Values.Where(s => s.Client == client))
                    previous.Stop();
            }

            if (_streams.Values.Count(s => !s.Stopped) >= Math.Max(1, max))
                return null;
            var lease = new Lease(this, client, path, viewer, start, transcoded, time.GetUtcNow());
            _streams[lease.Id] = lease;
            return lease;
        }
    }

    /// <summary>One stream: counts what is sent, stops when asked, leaves the list once disposed of.</summary>
    public sealed class Lease : IDisposable
    {
        private readonly VideoStreams _owner;
        private readonly CancellationTokenSource _stop = new();
        private readonly StreamActivity _activity;
        private long _bytes;

        internal Lease(VideoStreams owner, string? client, string path, StreamViewer viewer, double start, bool transcoded, DateTimeOffset started)
        {
            _owner = owner;
            Client = client;
            _activity = new StreamActivity(Guid.NewGuid(), path, viewer, start, transcoded, started, 0);
        }

        public Guid Id => _activity.Id;
        internal string? Client { get; }

        /// <summary>Cancelled when a newer stream of the same player replaced this one.</summary>
        public CancellationToken Stopping => _stop.Token;

        internal bool Stopped => _stop.IsCancellationRequested;

        public void Sent(int bytes) => Interlocked.Add(ref _bytes, bytes);

        internal StreamActivity Snapshot() => _activity with { Bytes = Interlocked.Read(ref _bytes) };

        internal void Stop()
        {
            try
            {
                _stop.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Ended in the meantime.
            }
        }

        public void Dispose()
        {
            _owner._streams.TryRemove(Id, out _);
            _stop.Dispose();
        }
    }
}
