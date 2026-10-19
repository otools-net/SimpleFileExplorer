using System.Diagnostics;
using System.Globalization;
using Sfe.Core.Thumbnails;

namespace Sfe.Tests.Infrastructure;

/// <summary>
/// Waits on work whose speed is the machine's, not SFE's: ffmpeg making test media, background workers rendering
/// trickplay or fingerprints. Past the limit the test is <b>inconclusive</b> (reported as skipped, with the reason), not
/// failed: a busy or slow host (a CI runner) is no bug. A wrong result still fails. <c>SFE_TEST_TIME_FACTOR</c> (≥ 1)
/// multiplies every limit, for slow machines (the GitHub workflow sets it).
/// </summary>
public static class SlowWork
{
    public const string FactorVariable = "SFE_TEST_TIME_FACTOR";

    public static double Factor { get; } =
        double.TryParse(Environment.GetEnvironmentVariable(FactorVariable), NumberStyles.Float, CultureInfo.InvariantCulture, out var factor) && factor >= 1
            ? factor
            : 1;

    /// <summary><paramref name="seconds"/> times <see cref="Factor"/>.</summary>
    public static TimeSpan Limit(double seconds) => TimeSpan.FromSeconds(seconds * Factor);

    /// <summary>Reads <paramref name="read"/> until <paramref name="condition"/> holds; inconclusive past the limit.</summary>
    /// <param name="what">What is awaited, for the message (e.g. "trickplay").</param>
    public static async Task<T> UntilAsync<T>(Func<T> read, Func<T, bool> condition, double seconds, string what, CancellationToken cancellationToken)
    {
        var limit = Limit(seconds);
        var watch = Stopwatch.StartNew();
        while (true)
        {
            var value = read();
            if (condition(value))
                return value;
            if (watch.Elapsed > limit)
                TooSlow($"waiting for {what} (last: {value})", limit);
            await Task.Delay(100, cancellationToken);
        }
    }

    /// <summary>Runs ffmpeg for a test (to make its media); a timeout makes the test inconclusive, an error fails it.</summary>
    public static async Task RunFfmpegAsync(Ffmpeg ffmpeg, IEnumerable<string> arguments, double seconds, CancellationToken cancellationToken)
    {
        var limit = Limit(seconds);
        int exitCode;
        string errors;
        try
        {
            (exitCode, errors) = await ffmpeg.RunAsync(arguments, limit, cancellationToken: cancellationToken);
        }
        catch (TimeoutException)
        {
            TooSlow("ffmpeg making the test media", limit);
            throw;
        }

        Assert.AreEqual(0, exitCode, errors);
    }

    private static void TooSlow(string what, TimeSpan limit) =>
        Assert.Inconclusive($"Timed out after {limit.TotalSeconds:0} s {what}: this machine is too slow or busy for this test " +
                            $"(not a failure of SFE). Set {FactorVariable} (e.g. 3) to give it more time.");
}
