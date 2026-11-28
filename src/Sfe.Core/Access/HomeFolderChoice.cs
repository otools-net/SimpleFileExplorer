namespace Otools.Net.Sfe.Core.Access;

/// <summary>An account's home folder in a library, and where it comes from.</summary>
/// <param name="Own">The folder the account chose (root-relative); null when it chose none.</param>
/// <param name="Default">Its groups' default, used when it chose none.</param>
public sealed record HomeFolderChoice(string? Own, GroupHomeFolder? Default)
{
    /// <summary>The home folder in effect (root-relative); null for none.</summary>
    public string? Path => Own ?? Default?.Path;
}
