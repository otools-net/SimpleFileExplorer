using System.Net;
using Microsoft.Extensions.Options;
using Sfe.Core.Access;
using Sfe.Core.Options;

namespace Sfe.Api.Security;

/// <summary>
/// Visitors on the server's own computer, for what only they may skip: the setup code of a new install
/// (<see cref="SetupCodeWaivedAsync"/>) and the one-time sign-in link of <see cref="FastSignIn"/>.
/// </summary>
public static class LocalVisitor
{
    /// <summary>
    /// The request comes straight from this computer: a loopback connection, not passed on by a proxy (a local proxy
    /// would bring other computers' visitors), for a loopback host name. The host name keeps out pages of other sites
    /// that make their own name point at 127.0.0.1 (DNS rebinding) to reach the server through the visitor's browser.
    /// </summary>
    public static bool IsOnThisComputer(HttpContext context)
    {
        var request = context.Request;
        if (context.Connection.RemoteIpAddress is not { } address || !IPAddress.IsLoopback(address)
            || request.Headers.ContainsKey("X-Forwarded-For") || request.Headers.ContainsKey("X-Original-For") || request.Headers.ContainsKey("Forwarded"))
            return false;

        var host = request.Host.Host;
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
               || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
               || (IPAddress.TryParse(host.Trim('[', ']'), out var named) && IPAddress.IsLoopback(named));
    }

    /// <summary>
    /// The setup may skip the code for this visitor: a new install (<see cref="SetupService.IsFreshAsync"/>) set up
    /// from this computer, unless <see cref="SetupOptions.LocalWithoutCode"/> is off.
    /// </summary>
    public static async Task<bool> SetupCodeWaivedAsync(HttpContext context)
    {
        var services = context.RequestServices;
        return services.GetRequiredService<IOptionsMonitor<SetupOptions>>().CurrentValue.LocalWithoutCode
               && IsOnThisComputer(context)
               && await services.GetRequiredService<SetupService>().IsFreshAsync(context.RequestAborted);
    }
}
