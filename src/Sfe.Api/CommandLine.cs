namespace Sfe.Api;

/// <summary>Switches of the program's own (not configuration keys), read before the builder sees the arguments.</summary>
internal static class CommandLine
{
    /// <summary>
    /// The arguments without <paramref name="name"/> (<c>--name</c> or <c>/name</c>, any case), which the configuration
    /// would read as a key taking the next argument as its value.
    /// </summary>
    public static string[] TakeSwitch(string[] args, string name, out bool present)
    {
        present = args.Any(Is);
        return present ? args.Where(a => !Is(a)).ToArray() : args;

        bool Is(string arg) => string.Equals(arg, "--" + name, StringComparison.OrdinalIgnoreCase)
                               || string.Equals(arg, "/" + name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary><c>--help</c>, <c>-h</c>, <c>-?</c>, <c>/?</c> or <c>/help</c> anywhere: the help, and nothing else.</summary>
    public static bool AsksForHelp(string[] args) =>
        args.Any(a => a.ToLowerInvariant() is "--help" or "-h" or "-?" or "/?" or "/help");

    /// <summary>
    /// What <c>--help</c> prints, grouped: the program's switches, then the settings most often given on the command line
    /// (any configuration key is accepted; docs/configuration.md lists them all). ASCII, within 80 columns.
    /// </summary>
    public static string HelpText(string version) => $"""
        Simple File Explorer {version}

        Usage: Sfe.Api [options] [--Setting value | --Setting=value ...]

        Start-up
          {BrowserLauncher.NoBrowserArgument,-34}Don't open the browser at start (portable
          {"",-34}packages), whatever the settings say
          {StartupLogo.NoLogoArgument,-34}Don't show the SFE logo at start
          {"--help, -h, -?",-34}Show this help and stop

        Windows service (run as administrator)
          {WindowsServiceSetup.InstallArgument + " [--name SFE]",-34}Register SFE as a service starting with
          {"",-34}the computer, and start it
          {WindowsServiceSetup.UninstallArgument + " [--name SFE]",-34}Stop and remove that service

        Main settings (win over the files and the setup page)
          {"--urls <addresses>",-34}Listening addresses, e.g. http://*:8080
          {"--Sfe:Mode <Local|External>",-34}SFE for this computer, or opened by others
          {"--Sfe:Root <folder>",-34}Library folder of the first space
          {"--Sfe:OpenBrowser <true|false>",-34}Open the browser at start
          {"--Sfe:Thumbnails:FfmpegPath",-34}Path to ffmpeg (default: from the PATH)

        Database
          {"--Sfe:Database:Provider",-34}Sqlite or SqlServer
          {"--ConnectionStrings:Sfe <cs>",-34}The first space's index

        Setup and sign-in
          {"--Sfe:Setup:Code <code>",-34}Setup code (at least 12 characters)
          {"--Sfe:Setup:Recover true",-34}Reset a lost administrator password
          {"--Sfe:Security:RequireHttps",-34}true when SFE is only used over https

        Every setting is described in docs/configuration.md.
        """;
}
