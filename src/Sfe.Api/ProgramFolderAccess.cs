using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Otools.Net.Sfe.Api;

/// <summary>
/// Who may change the folder SFE is installed in (Windows). A service runs with the rights of its account (Local
/// System): whoever can replace its program, or edit the settings beside it (the ffmpeg path), runs anything with those
/// rights. Asked before the service is installed (<see cref="WindowsServiceSetup"/>).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ProgramFolderAccess
{
    /// <summary>Rights that let their holder replace or add a file, or give themselves the right to.</summary>
    private const FileSystemRights Changing =
        FileSystemRights.WriteData
        | FileSystemRights.AppendData
        | FileSystemRights.WriteExtendedAttributes
        | FileSystemRights.WriteAttributes
        | FileSystemRights.Delete
        | FileSystemRights.DeleteSubdirectoriesAndFiles
        | FileSystemRights.ChangePermissions
        | FileSystemRights.TakeOwnership;

    /// <summary>The groups every ordinary account of the computer belongs to.</summary>
    private static readonly WellKnownSidType[] Everybody =
    [
        WellKnownSidType.WorldSid,
        WellKnownSidType.AuthenticatedUserSid,
        WellKnownSidType.BuiltinUsersSid,
        WellKnownSidType.InteractiveSid,
        WellKnownSidType.NetworkSid,
        WellKnownSidType.BuiltinGuestsSid,
        WellKnownSidType.AnonymousSid,
    ];

    /// <summary>
    /// The groups of ordinary accounts (Users, Authenticated Users, Everyone…) allowed to change
    /// <paramref name="folder"/> or what is in it, by their names on this computer; empty when only administrators,
    /// the system and named accounts may.
    /// </summary>
    public static IReadOnlyList<string> OrdinaryAccountsThatMayChange(string folder) =>
        OrdinaryAccountsThatMayChange(new DirectoryInfo(folder).GetAccessControl(AccessControlSections.Access));

    internal static IReadOnlyList<string> OrdinaryAccountsThatMayChange(DirectorySecurity security)
    {
        var everybody = Everybody.Select(type => new SecurityIdentifier(type, null)).ToList();
        return security
            .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Where(rule => rule.AccessControlType == AccessControlType.Allow && (rule.FileSystemRights & Changing) != 0)
            .Select(rule => (SecurityIdentifier)rule.IdentityReference)
            .Where(everybody.Contains)
            .Distinct()
            .Select(NameOf)
            .ToList();
    }

    private static string NameOf(SecurityIdentifier account)
    {
        try
        {
            return account.Translate(typeof(NTAccount)).Value;
        }
        catch (IdentityNotMappedException)
        {
            return account.Value;
        }
    }
}
