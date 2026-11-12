namespace Sfe.Api.Maintenance;

/// <param name="Value">The value the server uses now (from whichever source gives it).</param>
/// <param name="Locked">A source after the installation file gives it (an environment variable, the command line...):
/// the page can't change it.</param>
/// <param name="LockedBy">That source, in words.</param>
public sealed record InstallationField(string? Value, bool Locked, string? LockedBy);
