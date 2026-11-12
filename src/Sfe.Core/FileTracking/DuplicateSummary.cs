namespace Sfe.Core.FileTracking;

/// <param name="Groups">Groups of two or more identical files.</param>
/// <param name="Files">Files in those groups.</param>
/// <param name="WastedBytes">Space taken by all copies but one of each group.</param>
/// <param name="Unfingerprinted">Indexed files (in the whole index) without a fingerprint, which cannot be
/// compared.</param>
public sealed record DuplicateSummary(int Groups, int Files, long WastedBytes, int Unfingerprinted);
