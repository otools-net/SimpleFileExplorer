namespace Otools.Net.Sfe.Core.Options;

public sealed class SearchOptions
{
    public int DefaultPageSize { get; set; } = 120;
    public int MaxPageSize { get; set; } = 1000;

    /// <summary>
    /// The web client offers to put the typed query into words (button in the search box). Only a client feature:
    /// off hides the button for everyone using the library.
    /// </summary>
    public bool Explain { get; set; } = true;

    /// <summary>
    /// Searches each browser remembers and suggests again as the box is typed in, newest first; 0 = none (nothing
    /// is suggested or recorded). Kept by each browser (visitors without an account have no other place), so the server
    /// only sends the number.
    /// </summary>
    public int HistorySize { get; set; } = 10;
}
