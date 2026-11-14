using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace Otools.Net.Sfe.Api;

/// <summary>
/// <c>Sfe.Api --install-service [--name SFE]</c> and <c>--uninstall-service</c>: registers this program as a Windows
/// service that starts with the computer, so nobody has to type <c>sc.exe</c> commands. <c>install-service.cmd</c> next
/// to the program runs it with administrator rights. The service keeps this folder's settings and data.
/// </summary>
internal static class WindowsServiceSetup
{
    public const string InstallArgument = "--install-service";
    public const string UninstallArgument = "--uninstall-service";

    /// <summary>Installs the service although ordinary accounts may change the program's folder.</summary>
    public const string OpenFolderArgument = "--allow-open-folder";
    private const string DefaultName = "SFE";

    /// <summary>Runs the command when <paramref name="args"/> holds one.</summary>
    /// <returns>The process's exit code; null when there is no such command (start the app).</returns>
    public static int? TryRun(string[] args)
    {
        var install = args.Contains(InstallArgument, StringComparer.OrdinalIgnoreCase);
        var uninstall = args.Contains(UninstallArgument, StringComparer.OrdinalIgnoreCase);
        if (!install && !uninstall)
            return null;

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine(
                "Windows services exist on Windows only. On Linux, run SFE as a systemd service (see "
                    + "docs/installation.md)."
            );
            return 1;
        }

        var nameIndex = Array.FindIndex(args, a => a.Equals("--name", StringComparison.OrdinalIgnoreCase));
        var name = nameIndex >= 0 && nameIndex + 1 < args.Length ? args[nameIndex + 1] : DefaultName;
        var allowOpenFolder = args.Contains(OpenFolderArgument, StringComparer.OrdinalIgnoreCase);
        return install ? Install(name, allowOpenFolder) : Uninstall(name);
    }

    [SupportedOSPlatform("windows")]
    private static int Install(string name, bool allowOpenFolder)
    {
        if (!IsAdministrator())
            return NeedsAdministrator();

        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("The program's path is unknown.");
        if (!allowOpenFolder && FolderIsOpen(Path.GetDirectoryName(exe)!))
            return 1;
        if (
            Sc($"create \"{name}\" binPath= \"\\\"{exe}\\\"\" start= auto DisplayName= \"Simple File Explorer\"")
            is not 0
        )
        {
            return 1;
        }

        Sc($"description \"{name}\" \"SFE - Simple File Explorer: the web file explorer of the media library.\"");
        // Started again a minute after a crash, at most once a day.
        Sc($"failure \"{name}\" reset= 86400 actions= restart/60000");
        if (Sc($"start \"{name}\"") is not 0)
            return 1;

        Console.WriteLine();
        Console.WriteLine($"SFE now runs as the Windows service \"{name}\" and starts with the computer.");
        Console.WriteLine(
            "It runs as the Local System account: it can read the local drives, not the network drives mapped by a "
                + "user."
        );
        Console.WriteLine("Open http://localhost:5080 (or the port you chose). Remove it with uninstall-service.cmd.");
        return 0;
    }

    /// <summary>
    /// Says, and answers true, when ordinary accounts of the computer may change <paramref name="folder"/>: as a
    /// service SFE runs as Local System, and whoever replaces its program or edits its settings would run anything as
    /// that account.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static bool FolderIsOpen(string folder)
    {
        IReadOnlyList<string> accounts;
        try
        {
            accounts = ProgramFolderAccess.OrdinaryAccountsThatMayChange(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SystemException)
        {
            Console.Error.WriteLine(
                $"Cannot read who may change {folder} ({ex.Message}): the service is not installed."
            );
            Console.Error.WriteLine($"To install it anyway, add {OpenFolderArgument}.");
            return true;
        }

        if (accounts.Count == 0)
            return false;

        Console.Error.WriteLine(
            $"""
            The service is not installed: {string.Join(", ", accounts)} may change the files in
              {folder}
            A service runs as Local System, which can do anything on this computer: whoever can replace SFE's program
            or edit its settings in that folder would get the same power.

            Either move SFE to a folder only administrators may change (for example under C:\Program Files), or close
            this one, from a console opened as administrator:

              icacls "{folder}" /inheritance:r /grant:r "*S-1-5-32-544:(OI)(CI)F" "*S-1-5-18:(OI)(CI)F"

            (only Administrators and the system keep access; add /grant "name:(OI)(CI)RX" for who must read it).
            Then run this again. To install it as it is, at your own risk, add {OpenFolderArgument}.
            """
        );
        return true;
    }

    [SupportedOSPlatform("windows")]
    private static int Uninstall(string name)
    {
        if (!IsAdministrator())
            return NeedsAdministrator();

        Sc($"stop \"{name}\"");
        if (Sc($"delete \"{name}\"") is not 0)
            return 1;
        Console.WriteLine($"The Windows service \"{name}\" is removed. Its settings and data stay in this folder.");
        return 0;
    }

    [SupportedOSPlatform("windows")]
    private static bool IsAdministrator() =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    private static int NeedsAdministrator()
    {
        Console.Error.WriteLine(
            "Installing or removing a service needs administrator rights: run install-service.cmd, "
                + "or this command from a console opened as administrator."
        );
        return 1;
    }

    private static int Sc(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("sc.exe", arguments) { UseShellExecute = false })
            ?? throw new InvalidOperationException("Cannot start sc.exe.");
        process.WaitForExit();
        return process.ExitCode;
    }
}
