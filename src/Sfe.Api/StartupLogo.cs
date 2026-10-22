namespace Sfe.Api;

/// <summary>
/// SFE's name and version drawn in the console at start, for someone watching the window. Not with
/// <see cref="NoLogoArgument"/>, nor when nobody watches: a service, or output going to a file, a pipe, the journal or
/// <c>docker logs</c>.
/// </summary>
internal static class StartupLogo
{
    /// <summary>Command-line switch that leaves the logo out.</summary>
    public const string NoLogoArgument = "--no-logo";

    /// <summary>The arguments without <see cref="NoLogoArgument"/>.</summary>
    public static string[] TakeNoLogo(string[] args, out bool noLogo) => CommandLine.TakeSwitch(args, "no-logo", out noLogo);

    /// <summary>The logo, ASCII only (any console code page shows it).</summary>
    public static string Text(string version) => $"""

         ____   _____  _____
        / ___| |  ___|| ____|
        \___ \ | |_   |  _|
         ___) ||  _|  | |___
        |____/ |_|    |_____|   Simple File Explorer {version}

        """;

    public static void ShowIfWanted(bool noLogo)
    {
        if (noLogo || !Environment.UserInteractive || Console.IsOutputRedirected)
            return;
        Console.Out.WriteLine(Text(AppVersion.Version));
        Console.Out.Flush();
    }
}
