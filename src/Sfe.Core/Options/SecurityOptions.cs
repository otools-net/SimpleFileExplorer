namespace Sfe.Core.Options;

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
    /// <c>X-Forwarded-Proto</c> are believed, besides this computer's own (always): the visitor's address then counts
    /// for the sign-in limits, and https at the proxy marks the cookie secure.
    /// </summary>
    public List<string> TrustedProxies { get; set; } = [];

    /// <summary>
    /// What a link in a library (symbolic link, junction) leading outside it allows: <c>Follow</c> (reads and writes,
    /// as always) or <c>ReadOnly</c> (nothing is written behind it). Only in the configuration: it decides what the
    /// server's account may be made to write.
    /// </summary>
    public LinksOutsideLibrary LinksOutsideLibrary { get; set; } = LinksOutsideLibrary.Follow;

    /// <summary>
    /// With <see cref="LinksOutsideLibrary"/> <c>ReadOnly</c>: folders outside the libraries that links may still be
    /// written through (full paths, e.g. a second drive added to a library by a link).
    /// </summary>
    public List<string> LinkTargets { get; set; } = [];

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
