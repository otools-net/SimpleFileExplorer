using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Sfe.Api.Security;

/// <summary>
/// The link that sets a new install up without the setup code: the address of the browser SFE opens at its first
/// start, also printed in the console. A connection from this computer is not enough to skip the code
/// (<see cref="LocalVisitor.SetupCodeWaivedAsync"/>): a tunnel on the computer (<c>ssh -R</c>, <c>ngrok tcp</c>) brings
/// strangers in as loopback visitors, and every user of a shared computer is one. Only who started SFE, or reads its
/// console, has the link. One per run of the server, kept in memory; it stops mattering once an account exists.
/// </summary>
public sealed class LocalSetupLink
{
    /// <summary>The query parameter of the link (<c>http://localhost:5080/?setup=…</c>); the client keeps its value.</summary>
    public const string Parameter = "setup";

    /// <summary>The request header the client sends the link's token back in, on the setup and installation routes.</summary>
    public const string Header = "X-SFE-Setup-Link";

    private const int TokenBytes = 16;

    /// <summary>The token of this run's link.</summary>
    public string Token { get; } = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));

    /// <summary>The address to open: <paramref name="url"/> (the app's, ending with <c>/</c>) with the token.</summary>
    public string UrlFor(string url) => $"{url}?{Parameter}={Token}";

    /// <summary>The request carries this run's link.</summary>
    public bool IsCarriedBy(HttpRequest request) =>
        request.Headers[Header].ToString() is { Length: > 0 } given
        && CryptographicOperations.FixedTimeEquals(Hash(given), Hash(Token));

    private static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
