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

    public bool HasUsableCode => Code is { } code && code.Trim().Length >= MinimumCodeLength;
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

    /// <summary>Failed sign-ins in a row after which a local account is locked for <see cref="LockoutMinutes"/>.</summary>
    public int MaxFailedSignIns { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 15;

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
