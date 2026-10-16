using System.Diagnostics;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Sfe.Api.Endpoints;
using Sfe.Api.Security;
using Sfe.Core.Options;
using Sfe.Core.Settings;

namespace Sfe.Api;

/// <summary>
/// Opens the app in the default browser once it listens, for a portable package started by hand (a double click on
/// Windows, the AppImage on a desktop): its user shouldn't have to know the address. Never for a service; off with
/// <see cref="SfeOptions.OpenBrowser"/> (configuration or System › Start-up) or <see cref="NoBrowserArgument"/>. With
/// <see cref="SfeOptions.FastSignIn"/>, the browser opens a one-time link that signs the only administrator in.
/// </summary>
internal static class BrowserLauncher
{
    /// <summary>Command-line switch that keeps the browser closed, whatever the settings say.</summary>
    public const string NoBrowserArgument = "--no-browser";

    /// <summary>The arguments without <see cref="NoBrowserArgument"/>, which the configuration would read as a key taking the next argument as its value.</summary>
    public static string[] TakeNoBrowser(string[] args, out bool noBrowser)
    {
        noBrowser = args.Any(IsNoBrowser);
        return noBrowser ? args.Where(a => !IsNoBrowser(a)).ToArray() : args;

        static bool IsNoBrowser(string arg) => string.Equals(arg, NoBrowserArgument, StringComparison.OrdinalIgnoreCase)
                                               || string.Equals(arg, "/no-browser", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task OpenIfWantedAsync(WebApplication app, bool noBrowser, bool updateDue)
    {
        if (noBrowser || !PortableBuild.IsPortable
            || SetupEndpoints.DetectHosting() is not (HostingKind.Portable or HostingKind.AppImage)
            || !Environment.UserInteractive)
            return;

        // The configuration, then System › Start-up (settings.json); readable before an update too (nothing is written).
        if (!app.Services.GetRequiredService<SettingLayers>().ServerLevel().OpenBrowser)
            return;

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault();
        if (BrowserUrl(address) is not { } url)
            return;
        var target = url;
        try
        {
            // Not while an update is due: the session routes wait for it.
            if (!updateDue && await app.Services.GetRequiredService<FastSignIn>().IssueAsync() is { } token)
                target = $"{url}{FastSignIn.Route}?token={token}";
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Fast sign-in skipped: cannot read the accounts");
        }

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            app.Logger.LogInformation(ex, "Cannot open a browser: open {Url} yourself", url);
        }
    }

    /// <summary>A listening address as a browser on this computer reaches it (<c>http://[::]:5080</c> → <c>http://localhost:5080/</c>).</summary>
    internal static string? BrowserUrl(string? address)
    {
        if (string.IsNullOrEmpty(address))
            return null;
        var url = address.Replace("://*", "://localhost").Replace("://+", "://localhost").Replace("://0.0.0.0", "://localhost").Replace("://[::]", "://localhost");
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri.GetLeftPart(UriPartial.Authority) + "/" : null;
    }
}
