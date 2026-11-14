using Otools.Net.Sfe.Core.Options;

namespace Otools.Net.Sfe.Core.Access;

/// <param name="Code">The setup code of the environment.</param>
/// <param name="LibraryName">New name of the first library; empty keeps it.</param>
/// <param name="FastSignIn">
/// Sign the administrator in at once in the browser SFE opens when it starts (<see cref="SfeOptions.FastSignIn"/>):
/// for an app used on this computer alone.
/// </param>
public sealed record SetupInput(
    string? Code,
    string? UserName,
    string? Password,
    string? DisplayName = null,
    string? LibraryName = null,
    bool FastSignIn = false
);
