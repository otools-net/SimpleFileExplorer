using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Otools.Net.Sfe.Tests.Infrastructure;

/// <summary>
/// A file the test's account may not read for as long as this lives: a Windows ACL denying reading, elsewhere a mode
/// without read. Disposing gives reading back (so the test library can be deleted).
/// </summary>
public sealed class UnreadableFile : IDisposable
{
    private readonly string _file;

    private UnreadableFile(string file)
    {
        _file = file;
        if (OperatingSystem.IsWindows())
            ChangeRule(file, add: true);
        else
            File.SetUnixFileMode(file, UnixFileMode.None);
    }

    /// <summary>The file at <paramref name="fullPath"/> made unreadable; inconclusive as root.</summary>
    public static UnreadableFile Make(string fullPath)
    {
        if (!OperatingSystem.IsWindows() && Environment.UserName == "root")
            Assert.Inconclusive("root reads everything.");
        return new UnreadableFile(fullPath);
    }

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
            ChangeRule(_file, add: false);
        else
            File.SetUnixFileMode(_file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [SupportedOSPlatform("windows")]
    private static void ChangeRule(string file, bool add)
    {
        var info = new FileInfo(file);
        var security = info.GetAccessControl();
        var rule = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ReadData,
            AccessControlType.Deny
        );
        if (add)
            security.AddAccessRule(rule);
        else
            security.RemoveAccessRule(rule);
        info.SetAccessControl(security);
    }
}
