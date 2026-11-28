namespace Otools.Net.Sfe.Api.Endpoints;

/// <param name="Path">The home folder in effect, relative to the library root (empty for the root); null for none.</param>
/// <param name="Name">The folder's name (the library's name for the root); null for none.</param>
/// <param name="Exists">It is a folder the account sees: false once it was moved, deleted, never made, or hidden.</param>
/// <param name="Chosen">The account chose it; false when it is its groups' default (or there is none).</param>
/// <param name="Group">The group whose default the account gets when it chooses none; null for none.</param>
/// <param name="GroupPath">That default for this account (relative to the library root), what removing the choice
/// goes back to; null for none.</param>
public sealed record HomeFolderView(
    string? Path,
    string? Name,
    bool Exists,
    bool Chosen,
    string? Group,
    string? GroupPath
);

/// <param name="Path">A folder of the library, relative to its root (empty for the root).</param>
public sealed record HomeFolderInput(string? Path);
