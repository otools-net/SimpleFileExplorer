namespace Otools.Net.Sfe.Client.Tests.Infrastructure;

/// <summary>
/// Keeps each value reported, at once and in order (<see cref="Progress{T}"/> posts them later, out of the test's
/// reach).
/// </summary>
public sealed class SyncProgress<T>(List<T> reported) : IProgress<T>
{
    public void Report(T value)
    {
        lock (reported)
            reported.Add(value);
    }
}
