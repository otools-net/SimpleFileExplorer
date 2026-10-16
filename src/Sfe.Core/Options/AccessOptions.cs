namespace Sfe.Core.Options;

/// <summary>
/// The first administrator. Set in the environment only (configuration files, environment variables): whoever can
/// set it controls the server anyway, which is what makes it safe to accept from a web page.
/// </summary>
public sealed class SetupOptions
{
    /// <summary>Shortest setup code accepted: a short one could be guessed through the setup page.</summary>
    public const int MinimumCodeLength = 12;

    /// <summary>
    /// The code the setup page asks for before it creates the first administrator (<c>Sfe__Setup__Code</c>). Without
    /// one, the app only shows how to set it. Ignored once an administrator exists, unless <see cref="Recover"/> is on.
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// Opens the setup page again, with the code, to reset the password of a local administrator or create a new
    /// one: the way back in after a lost password. Turn it off afterwards.
    /// </summary>
    public bool Recover { get; set; }

    /// <summary>
    /// Where the server keeps the setup code it makes itself when none is configured (<see cref="Access.GeneratedSetupCode"/>),
    /// so that nobody has to edit a file before the first start. Relative paths are resolved against the content root.
    /// </summary>
    public string CodeFile { get; set; } = "data/setup-code.txt";

    /// <summary><see cref="Code"/> comes from <see cref="CodeFile"/>, not from the configuration.</summary>
    public bool CodeIsGenerated { get; internal set; }

    public bool HasUsableCode => Code is { } code && code.Trim().Length >= MinimumCodeLength;

    /// <summary>
    /// <paramref name="input"/> is the setup code, compared in constant time. A generated code is compared without its
    /// dashes, spaces and case (people retype it from a console); a configured one exactly, trimmed.
    /// </summary>
    public bool Matches(string? input)
    {
        if (!HasUsableCode)
            return false;
        return CodeIsGenerated
            ? Access.PasswordHashing.SameSecret(Access.GeneratedSetupCode.Normalize(input), Access.GeneratedSetupCode.Normalize(Code))
            : Access.PasswordHashing.SameSecret(input?.Trim() ?? string.Empty, Code!.Trim());
    }
}

/// <summary>Accounts, sign-in and guests.</summary>
public sealed class SecurityOptions
{
    /// <summary>
    /// Libraries may let visitors in without an account (each library decides, in its access settings); off forbids
    /// it for every library.
    /// </summary>
    public bool AnonymousGuests { get; set; } = true;

    /// <summary>
    /// Folder of the keys that sign the sign-in cookies and the links given to external players. Relative paths are
    /// resolved against the application content root. Losing it signs everyone out and breaks the links given out.
    /// </summary>
    public string KeysDirectory { get; set; } = "data/keys";

    /// <summary>Days a "keep me signed in" session lasts without being used.</summary>
    public int SessionDays { get; set; } = 30;

    /// <summary>Shortest password accepted for a local account.</summary>
    public int MinimumPasswordLength { get; set; } = 8;

    /// <summary>
    /// Failed sign-ins in a row to one account from one address, after which that address can't sign in to it for
    /// <see cref="LockoutMinutes"/>. Others still can: a stranger can't lock the administrator out.
    /// </summary>
    public int MaxFailedSignIns { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 15;

    /// <summary>
    /// Failed sign-ins in a row to a local account, from all addresses together, after which the account itself is
    /// locked for <see cref="LockoutMinutes"/>: guessing from many addresses at once. 0 = never.
    /// </summary>
    public int AccountLockoutFailures { get; set; } = 50;

    /// <summary>
    /// Reverse proxies (addresses, or networks such as <c>10.0.0.0/8</c>) whose <c>X-Forwarded-For</c> and
    /// <c>X-Forwarded-Proto</c> are believed, besides this computer's own (always): the visitor's address then counts for
    /// the sign-in limits, and https at the proxy marks the cookie secure.
    /// </summary>
    public List<string> TrustedProxies { get; set; } = [];

    /// <summary>
    /// The app is only used over https (directly or behind a proxy): the sign-in cookie is never sent over plain http,
    /// and browsers are told to use https only (HSTS).
    /// </summary>
    public bool RequireHttps { get; set; }

    /// <summary>Sign-in and setup attempts accepted per minute from one address.</summary>
    public int SignInsPerMinute { get; set; } = 10;

    /// <summary>
    /// Where accounts and passwords are checked, in order (<see cref="Access.IUserDirectory.Source"/>); empty = the
    /// local accounts only. A directory such as LDAP added later is listed here.
    /// </summary>
    public List<string> Directories { get; set; } = [];
}

/// <summary>Rights switched off for the whole server.</summary>
public sealed class RightOptions
{
    /// <summary>
    /// Rights nobody gets, not even administrators (e.g. <c>tracking.duplicatesRemove</c>, <c>files.*</c>): the
    /// functions they cover are gone from the server. See <see cref="Access.RightCatalog"/>.
    /// </summary>
    public List<string> Disabled { get; set; } = [];
}

/// <summary>Libraries other than the first one (tenants).</summary>
public sealed class TenantOptions
{
    /// <summary>
    /// Folders the libraries' roots may be chosen in (with their sub-folders); empty = <see cref="SfeOptions.Root"/>.
    /// </summary>
    public List<string> AllowedRoots { get; set; } = [];

    /// <summary>
    /// Connection string of a new library's index, with <c>{tenant}</c> standing for the library's short name. When
    /// empty, SQLite servers keep each index in <c>data/tenants/{tenant}.db</c>; SQL Server needs one (e.g.
    /// <c>Server=.;Database=SFE_{tenant};Trusted_Connection=True;TrustServerCertificate=True</c>). The first library
    /// keeps <c>ConnectionStrings:Sfe</c>.
    /// </summary>
    public string? ConnectionString { get; set; }
}

/// <summary>Backups of the databases and configuration files (Administration › System, and the update page).</summary>
public sealed class BackupOptions
{
    /// <summary>
    /// SQL Server only: the folder, as the database server sees it, where it writes each database's backup (<c>BACKUP
    /// DATABASE</c>) before SFE adds it to the zip file; empty = the server's default backup folder. When SFE cannot
    /// read that folder (another computer, no access), the zip names where the backup was left instead. SQLite
    /// databases are copied by SFE itself.
    /// </summary>
    public string? SqlServerFolder { get; set; }
}
