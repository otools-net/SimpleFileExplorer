using Otools.Net.Sfe.Core.Options;

namespace Otools.Net.Sfe.Core.Access;

/// <param name="Required">No server administrator yet: the setup page is the only one working.</param>
/// <param name="Available">The setup page can be used: <paramref name="Required"/>, or
/// <see cref="SetupOptions.Recover"/> is on.</param>
/// <param name="CodeConfigured">The environment gives a setup code long enough to be used.</param>
/// <param name="CodeTooShort">A setup code is set but shorter than
/// <see cref="SetupOptions.MinimumCodeLength"/>.</param>
/// <param name="Recover">The setup resets or adds a local administrator rather than starting from nothing.</param>
/// <param name="LibraryName">Name of the first library so far (the setup may rename it).</param>
/// <param name="CodeGenerated">The code is the one the server made itself (<see cref="GeneratedSetupCode"/>), shown in
/// its console.</param>
/// <param name="CodeFile">Where that code is kept, as configured (<see cref="SetupOptions.CodeFile"/>, usually relative
/// to SFE's folder).</param>
public sealed record SetupState(
    bool Required,
    bool Available,
    bool CodeConfigured,
    bool CodeTooShort,
    bool Recover,
    int MinimumCodeLength,
    string LibraryName,
    bool CodeGenerated = false,
    string? CodeFile = null
);
