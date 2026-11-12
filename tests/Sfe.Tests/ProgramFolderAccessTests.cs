using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Sfe.Api;

namespace Sfe.Tests;

/// <summary>Who may change the folder a Windows service is installed from (audit 2026-11, A2). Windows only.</summary>
[TestClass]
[SupportedOSPlatform("windows")]
public sealed class ProgramFolderAccessTests
{
    private const InheritanceFlags Everything = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

    [TestInitialize]
    public void Initialize()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Windows access lists exist on Windows only.");
    }

    [TestMethod]
    [DataRow(WellKnownSidType.BuiltinUsersSid, FileSystemRights.Modify)]
    [DataRow(WellKnownSidType.AuthenticatedUserSid, FileSystemRights.Modify)]
    [DataRow(WellKnownSidType.WorldSid, FileSystemRights.FullControl)]
    [DataRow(WellKnownSidType.BuiltinUsersSid, FileSystemRights.CreateFiles)]
    [DataRow(WellKnownSidType.BuiltinUsersSid, FileSystemRights.ChangePermissions)]
    [DataRow(WellKnownSidType.InteractiveSid, FileSystemRights.Write)]
    public void AFolderOrdinaryAccountsMayChangeIsReported(WellKnownSidType group, FileSystemRights rights)
    {
        var security = AdministratorsOnly();
        security.AddAccessRule(Rule(group, rights, AccessControlType.Allow));

        Assert.HasCount(1, ProgramFolderAccess.OrdinaryAccountsThatMayChange(security));
    }

    [TestMethod]
    public void AFolderOnlyAdministratorsChangeIsNot()
    {
        var security = AdministratorsOnly();
        security.AddAccessRule(Rule(WellKnownSidType.BuiltinUsersSid, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
        security.AddAccessRule(Rule(WellKnownSidType.WorldSid, FileSystemRights.Modify, AccessControlType.Deny));

        Assert.IsEmpty(ProgramFolderAccess.OrdinaryAccountsThatMayChange(security));
    }

    [TestMethod]
    public void EachGroupIsNamedOnce()
    {
        var security = AdministratorsOnly();
        security.AddAccessRule(Rule(WellKnownSidType.BuiltinUsersSid, FileSystemRights.CreateFiles, AccessControlType.Allow));
        security.AddAccessRule(Rule(WellKnownSidType.BuiltinUsersSid, FileSystemRights.AppendData, AccessControlType.Allow));
        security.AddAccessRule(Rule(WellKnownSidType.AuthenticatedUserSid, FileSystemRights.Modify, AccessControlType.Allow));

        Assert.HasCount(2, ProgramFolderAccess.OrdinaryAccountsThatMayChange(security));
    }

    [TestMethod]
    public void AFolderOfTheDiskIsRead()
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "sfe-tests", Guid.NewGuid().ToString("N")));
        try
        {
            var closed = AdministratorsOnly();
            closed.AddAccessRule(new FileSystemAccessRule(
                WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl, Everything, PropagationFlags.None, AccessControlType.Allow));
            folder.SetAccessControl(closed);
            Assert.IsEmpty(ProgramFolderAccess.OrdinaryAccountsThatMayChange(folder.FullName));

            closed.AddAccessRule(Rule(WellKnownSidType.BuiltinUsersSid, FileSystemRights.Modify, AccessControlType.Allow));
            folder.SetAccessControl(closed);
            Assert.HasCount(1, ProgramFolderAccess.OrdinaryAccountsThatMayChange(folder.FullName));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    /// <summary>An access list of its own (nothing inherited) where Administrators and the system have everything.</summary>
    private static DirectorySecurity AdministratorsOnly()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(Rule(WellKnownSidType.BuiltinAdministratorsSid, FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(Rule(WellKnownSidType.LocalSystemSid, FileSystemRights.FullControl, AccessControlType.Allow));
        return security;
    }

    private static FileSystemAccessRule Rule(WellKnownSidType group, FileSystemRights rights, AccessControlType type) =>
        new(new SecurityIdentifier(group, null), rights, Everything, PropagationFlags.None, type);
}
