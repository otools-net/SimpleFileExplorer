using System.Diagnostics;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Sfe.Api.Endpoints;
using Sfe.Core.Options;

namespace Sfe.Api;

/// <summary>
/// Opens the app in the default browser once it listens, for a portable package started by hand (a double click on
/// Windows, the AppImage on a desktop): its user shouldn't have to know the address. Never for a service, and off with
/// <see cref="SfeOptions.OpenBrowser"/>.
/// </summary>
internal static class BrowserLauncher
{
    public static void OpenIfWanted(WebApplication app)
    {
        if (!PortableBuild.IsPortable || !app.Configuration.GetValue($"{SfeOptions.SectionName}:{nameof(SfeOptions.OpenBrowser)}", true)
            || SetupEndpoints.DetectHosting() is not (HostingKind.Portable or HostingKind.AppImage)
            || !Environment.UserInteractive)
            return;

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault();
        if (BrowserUrl(address) is not { } url)
            return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
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
