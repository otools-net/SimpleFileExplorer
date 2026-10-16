using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sfe.Core.Data;
using Sfe.Core.Logs;
using Sfe.Core.Options;

namespace Sfe.Core.Access;

/// <param name="Required">No server administrator yet: the setup page is the only one working.</param>
/// <param name="Available">The setup page can be used: <paramref name="Required"/>, or <see cref="SetupOptions.Recover"/> is on.</param>
/// <param name="CodeConfigured">The environment gives a setup code long enough to be used.</param>
/// <param name="CodeTooShort">A setup code is set but shorter than <see cref="SetupOptions.MinimumCodeLength"/>.</param>
/// <param name="Recover">The setup resets or adds a local administrator rather than starting from nothing.</param>
/// <param name="LibraryName">Name of the first library so far (the setup may rename it).</param>
/// <param name="CodeGenerated">The code is the one the server made itself (<see cref="GeneratedSetupCode"/>), shown in its console.</param>
/// <param name="CodeFile">Where that code is kept, as configured (<see cref="SetupOptions.CodeFile"/>, usually relative to SFE's folder).</param>
public sealed record SetupState(bool Required, bool Available, bool CodeConfigured, bool CodeTooShort, bool Recover, int MinimumCodeLength, string LibraryName,
    bool CodeGenerated = false, string? CodeFile = null);

/// <param name="Code">The setup code of the environment.</param>
/// <param name="LibraryName">New name of the first library; empty keeps it.</param>
/// <param name="FastSignIn">
/// Sign the administrator in at once in the browser SFE opens when it starts (<see cref="SfeOptions.FastSignIn"/>):
/// for an app used on this computer alone.
/// </param>
public sealed record SetupInput(string? Code, string? UserName, string? Password, string? DisplayName = null, string? LibraryName = null,
    bool FastSignIn = false);

/// <summary>
/// The first administrator. Until one exists, nothing but the setup page works; the page needs the code set in the
/// environment (<see cref="SetupOptions.Code"/>), so only whoever runs the server can claim it.
/// </summary>
public sealed class SetupService(
    IDbContextFactory<AccessDbContext> dbFactory,
    AccessService accounts,
    AccessChanges changes,
    IOptionsMonitor<SetupOptions> options,
    GeneratedSetupCode generatedCode,
    ILogger<SetupService> logger)
{
    private (long Version, bool HasAdmin)? _hasAdmin;
    private (long Version, bool HasAccounts)? _hasAccounts;

    /// <summary>No server administrator yet. Asked on every request: cached until the next change of the accounts.</summary>
    public async Task<bool> IsRequiredAsync(CancellationToken cancellationToken = default)
    {
        var version = changes.Version;
        if (_hasAdmin is { } cached && cached.Version == version)
            return !cached.HasAdmin;

        var hasAdmin = await accounts.HasServerAdminAsync(cancellationToken);
        _hasAdmin = (version, hasAdmin);
        return !hasAdmin;
    }

    /// <summary>
    /// A new install: no account at all yet (not even a disabled one), and not in recovery. Its setup may skip the code
    /// for a visitor on this computer (<see cref="SetupOptions.LocalWithoutCode"/>, decided by the Api); an install
    /// whose administrators were all disabled or deleted still needs it.
    /// </summary>
    public async Task<bool> IsFreshAsync(CancellationToken cancellationToken = default)
    {
        if (options.CurrentValue.Recover || !await IsRequiredAsync(cancellationToken))
            return false;
        var version = changes.Version;
        if (_hasAccounts is { } cached && cached.Version == version)
            return !cached.HasAccounts;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var hasAccounts = await db.Users.AnyAsync(cancellationToken);
        _hasAccounts = (version, hasAccounts);
        return !hasAccounts;
    }

    public async Task<SetupState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var setup = options.CurrentValue;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var library = await db.Tenants.AsNoTracking().Where(t => t.IsDefault).Select(t => t.Name).FirstOrDefaultAsync(cancellationToken);
        var required = await IsRequiredAsync(cancellationToken);
        if (required || setup.Recover)
            setup = EnsureCode(setup);
        var code = setup.Code?.Trim();
        return new SetupState(
            required,
            required || setup.Recover,
            setup.HasUsableCode,
            !string.IsNullOrEmpty(code) && code.Length < SetupOptions.MinimumCodeLength,
            setup.Recover,
            SetupOptions.MinimumCodeLength,
            library ?? "Library",
            setup.CodeIsGenerated,
            setup.CodeFile);
    }

    /// <summary>
    /// Creates the first server administrator (or, in recovery, resets the password of a local one with that name,
    /// enables it and makes it an administrator) and puts it in the first library's Administrators group.
    /// </summary>
    /// <param name="codeWaived">
    /// The visitor may skip the code: a new install (<see cref="IsFreshAsync"/>, checked again here) set up from the
    /// server's own computer, as the Api decided.
    /// </param>
    public async Task<AccessResult<SessionUser>> CompleteAsync(SetupInput input, bool codeWaived = false, CancellationToken cancellationToken = default)
    {
        var setup = options.CurrentValue;
        if (!setup.Recover && !await IsRequiredAsync(cancellationToken))
            return AccessResult<SessionUser>.Conflict("The server is set up already: sign in instead.");
        codeWaived = codeWaived && await IsFreshAsync(cancellationToken);
        if (!codeWaived && !setup.HasUsableCode)
            return AccessResult<SessionUser>.Conflict("The server has no setup code: set one in its environment first.");
        if (!codeWaived && !setup.Matches(input.Code))
        {
            logger.LogSecurityWarning("Setup refused: wrong setup code");
            return AccessResult<SessionUser>.Invalid("code", "This is not the server's setup code.");
        }

        var libraryName = input.LibraryName?.Trim();
        if (libraryName is { Length: > AccessService.MaxNameLength })
            return AccessResult<SessionUser>.Invalid("libraryName", $"A space's name can have at most {AccessService.MaxNameLength} characters.");

        var tenant = await accounts.EnsureDefaultTenantAsync("Library", cancellationToken);
        var userName = input.UserName?.Trim() ?? string.Empty;
        var existing = setup.Recover ? await FindLocalAsync(userName, cancellationToken) : null;
        var result = existing is null
            ? await accounts.CreateUserAsync(new UserInput(userName, input.DisplayName, input.Password, IsServerAdmin: true), null, cancellationToken)
            : await accounts.UpdateUserAsync(existing.Id,
                new UserInput(existing.UserName, input.DisplayName ?? existing.DisplayName, input.Password, existing.Kind == UserKind.Guest ? UserKind.User : existing.Kind,
                    IsServerAdmin: true, Disabled: false, Unlock: true),
                null, cancellationToken);
        if (result.Status != AccessStatus.Ok)
            return result.As<SessionUser>();

        if (await accounts.GetBuiltInGroupAsync(tenant.Id, BuiltInGroup.Administrators, cancellationToken) is { } administrators)
            await accounts.AddToGroupAsync(result.Value!.Id, administrators.Id, cancellationToken);
        if (!string.IsNullOrEmpty(libraryName) && libraryName != tenant.Name && !setup.Recover)
            await accounts.UpdateTenantAsync(tenant.Id, new TenantInput(libraryName, GuestAccess: tenant.GuestAccess, Disabled: tenant.Disabled), cancellationToken);

        logger.LogSecurityWarning(setup.Recover
            ? "Recovery: {User} is a server administrator again. Turn Sfe:Setup:Recover off."
            : codeWaived
                ? "Setup done from the server's own computer, without the setup code: {User} is the server administrator."
                : "Setup done: {User} is the server administrator. Sfe:Setup:Code is no longer needed.", result.Value!.UserName);
        return AccessResult<SessionUser>.Ok((await accounts.GetSessionUserAsync(result.Value.Id, cancellationToken))!);
    }

    /// <summary>
    /// Without a code in the configuration, the one the server makes itself (Program.cs also does it at start-up and
    /// prints it): the setup page needs one.
    /// </summary>
    private SetupOptions EnsureCode(SetupOptions setup)
    {
        if (!string.IsNullOrWhiteSpace(setup.Code))
            return setup;
        try
        {
            generatedCode.Ensure();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Cannot write the setup code to {Path}: set Sfe:Setup:Code instead", generatedCode.FilePath);
        }

        return options.CurrentValue;
    }

    private async Task<UserAccount?> FindLocalAsync(string userName, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserName == userName && u.Source == UserAccount.LocalSource, cancellationToken);
    }
}
