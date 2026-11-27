namespace Otools.Net.Sfe.Core.Files;

/// <summary>
/// Why the disk refused the server's account, as the end of a sentence (<see cref="FileOperations.DiskFailure"/>):
/// reading for an <see cref="UnreadableException"/>, otherwise changing files.
/// </summary>
internal static class DiskRefusals
{
    public static string Reason(UnauthorizedAccessException ex) =>
        ex switch
        {
            UnreadableException { Inside: null } => "the server's account may not read it.",
            UnreadableException { Inside: { } inside } => $"the server's account may not read “{inside}” in it.",
            _ => "the server's account may not change files there (it needs write access to the library's folder).",
        };
}
