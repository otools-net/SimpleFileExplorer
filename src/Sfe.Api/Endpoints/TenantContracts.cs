namespace Otools.Net.Sfe.Api.Endpoints;

/// <param name="Root">Full path of the space's library folder on the server.</param>
/// <param name="Members">Accounts with a group in the space.</param>
/// <param name="Groups">Groups of the space, the built-in ones included.</param>
public sealed record TenantDetails(
    int Id,
    string Slug,
    string Name,
    bool GuestAccess,
    bool IsDefault,
    bool Disabled,
    string Root,
    IReadOnlyList<string> DisabledRights,
    int Members,
    int Groups
);

public sealed record FolderChoice(string Name, string Path);

/// <param name="Path">The folder listed; null for the list of allowed roots.</param>
/// <param name="Parent">The folder above, while it is still inside an allowed root; null at a root.</param>
public sealed record FolderPicker(string? Path, string? Parent, IReadOnlyList<FolderChoice> Folders);
