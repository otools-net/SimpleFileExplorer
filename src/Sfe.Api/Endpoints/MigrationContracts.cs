using Sfe.Api.Maintenance;
using Sfe.Core.Upgrades;

namespace Sfe.Api.Endpoints;

/// <param name="DatabaseVersion">The version the install belongs to; null when unknown (installs before
/// 1.37.0).</param>
/// <param name="Changes">The changelog of the versions after it up to this one, newest first (while an update is
/// due).</param>
/// <param name="Log">What the update did so far; empty for who doesn't send the setup code (it names folders of the
/// server).</param>
/// <param name="Error">Why the update stopped or the database can't be read; without the setup code, only that it
/// did.</param>
/// <param name="CodeConfigured">The server has a setup code long enough (configured, or made by itself): the update can
/// be confirmed.</param>
/// <param name="CodeGenerated">The code is the one the server made itself: shown in its console and kept in
/// <paramref name="CodeFile"/>.</param>
public sealed record MigrationInfo(
    MigrationStatus Status,
    string AppVersion,
    string? DatabaseVersion,
    IReadOnlyList<ChangelogEntry> Changes,
    IReadOnlyList<string> Log,
    string? Error,
    bool CodeConfigured,
    bool CodeTooShort,
    int MinimumCodeLength,
    HostingKind Hosting,
    bool CodeGenerated = false,
    string? CodeFile = null
);

public sealed record MigrationInput(string? Code);

/// <param name="Url">Where the browser downloads it, once, within <see cref="BackupDownloads.Lifetime"/>.</param>
public sealed record BackupLink(string Url, string FileName, long Size);
