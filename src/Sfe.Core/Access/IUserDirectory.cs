using Sfe.Core.Data;
using Sfe.Core.Options;

namespace Sfe.Core.Access;

/// <summary>
/// Where accounts and passwords are checked. The local accounts are one (<see cref="LocalUserDirectory"/>); a
/// directory such as LDAP is another, registered next to it and named in <see cref="SecurityOptions.Directories"/>.
/// Whatever answers, rights, groups and preferences hang on the account's row in <see cref="AccessDbContext.Users"/>:
/// a directory account gets one on its first sign-in (<see cref="SignInService"/>).
/// </summary>
public interface IUserDirectory
{
    /// <summary>Name in <see cref="UserAccount.Source"/> and in <see cref="SecurityOptions.Directories"/>.</summary>
    string Source { get; }

    Task<DirectoryResult> ValidateAsync(string userName, string password, CancellationToken cancellationToken);
}
