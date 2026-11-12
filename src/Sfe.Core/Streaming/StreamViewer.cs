namespace Sfe.Core.Streaming;

/// <summary>
/// Who watches a converted video: shown to the server's administrators (<see cref="VideoStreams.List"/>).
/// </summary>
/// <param name="Library">Name of the library the video is in.</param>
/// <param name="User">The account's name; null for a visitor without one.</param>
/// <param name="Address">The viewer's network address, when known.</param>
public sealed record StreamViewer(string Library, string? User, string? Address);
