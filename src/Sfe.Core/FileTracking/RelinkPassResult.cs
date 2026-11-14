namespace Otools.Net.Sfe.Core.FileTracking;

/// <param name="Relinked">Missing files whose tags went to their new place.</param>
/// <param name="Forgotten">Missing files dropped for being kept longer than the retention.</param>
/// <param name="Remaining">Missing files still waiting.</param>
public sealed record RelinkPassResult(int Relinked, int Forgotten, int Remaining);
