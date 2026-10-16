using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Sfe.Core.Access;
using Sfe.Core.Options;

namespace Sfe.Api.Security;

/// <summary>Whether the visitor may use the installation page (<see cref="InstallationAccess.CheckVisitorAsync"/>).</summary>
public enum InstallationVisitor
{
    Allowed,
    NotSignedIn,
    NotServerAdmin,
}

/// <summary>
/// The installation page's proof that its visitor knows the setup code: the code is checked once (rate-limited, as
/// sign-ins), then a token valid for <see cref="Lifetime"/> carries that proof through the folder picker's many requests.
/// The token holds a hash of the code, so a new code ends every token given for the old one.
/// </summary>
public sealed class InstallationAccess(IDataProtectionProvider protection, IOptionsMonitor<SetupOptions> setup)
{
    /// <summary>The request header carrying the token.</summary>
    public const string Header = "X-SFE-Installation";

    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    private readonly ITimeLimitedDataProtector _protector = protection.CreateProtector("Sfe.Installation.v1").ToTimeLimitedDataProtector();

    /// <summary>A token for <paramref name="code"/>; null when it is not the setup code.</summary>
    public string? Grant(string? code) =>
        setup.CurrentValue is var options && options.Matches(code) ? _protector.Protect(Fingerprint(options), Lifetime) : null;

    /// <summary>
    /// Who may use the installation page at all: anyone with the code until the first administrator exists (the setup
    /// wizard); afterwards only a signed-in server administrator, who still needs the code. The code alone would
    /// otherwise stay a key to the server forever (it is kept for updates), and the page runs programs.
    /// </summary>
    public static async Task<InstallationVisitor> CheckVisitorAsync(HttpContext context)
    {
        var services = context.RequestServices;
        if (await services.GetRequiredService<SetupService>().IsRequiredAsync(context.RequestAborted))
            return InstallationVisitor.Allowed;
        var state = await services.GetRequiredService<CurrentAccess>().GetAsync();
        if (state.User is null)
            return InstallationVisitor.NotSignedIn;
        return state.Access.IsServerAdmin ? InstallationVisitor.Allowed : InstallationVisitor.NotServerAdmin;
    }

    /// <summary>The request carries a valid token for the current setup code.</summary>
    public bool IsGranted(HttpContext context)
    {
        if (context.Request.Headers[Header].ToString() is not { Length: > 0 } token || setup.CurrentValue is not { HasUsableCode: true } options)
            return false;
        try
        {
            return _protector.Unprotect(token, out _) == Fingerprint(options);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return false;
        }
    }

    private static string Fingerprint(SetupOptions options) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(options.CodeIsGenerated ? GeneratedSetupCode.Normalize(options.Code) : options.Code!.Trim())));
}
