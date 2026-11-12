namespace Sfe.Core.Options;

/// <summary>
/// The first administrator. Set in the environment only (configuration files, environment variables): whoever can
/// set it controls the server anyway, which is what makes it safe to accept from a web page.
/// </summary>
public sealed class SetupOptions
{
    /// <summary>Shortest setup code accepted: a short one could be guessed through the setup page.</summary>
    public const int MinimumCodeLength = 12;

    /// <summary>
    /// The code the setup page asks for before it creates the first administrator (<c>Sfe__Setup__Code</c>). Without
    /// one, the app only shows how to set it. Ignored once an administrator exists, unless <see cref="Recover"/> is on.
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// Opens the setup page again, with the code, to reset the password of a local administrator or create a new
    /// one: the way back in after a lost password. Turn it off afterwards.
    /// </summary>
    public bool Recover { get; set; }

    /// <summary>
    /// Where the server keeps the setup code it makes itself when none is configured
    /// (<see cref="Access.GeneratedSetupCode"/>), so that nobody has to edit a file before the first start. Relative
    /// paths are resolved against the content root.
    /// </summary>
    public string CodeFile { get; set; } = "data/setup-code.txt";

    /// <summary>
    /// A new install (no account yet) is set up without the code from a browser on the server's own computer: whoever
    /// sits there runs the server anyway. From any other computer, or once an account exists, the code is still asked.
    /// </summary>
    public bool LocalWithoutCode { get; set; } = true;

    /// <summary><see cref="Code"/> comes from <see cref="CodeFile"/>, not from the configuration.</summary>
    public bool CodeIsGenerated { get; internal set; }

    public bool HasUsableCode => Code is { } code && code.Trim().Length >= MinimumCodeLength;

    /// <summary>
    /// <paramref name="input"/> is the setup code, compared in constant time. A generated code is compared without its
    /// dashes, spaces and case (people retype it from a console); a configured one exactly, trimmed.
    /// </summary>
    public bool Matches(string? input)
    {
        if (!HasUsableCode)
            return false;
        return CodeIsGenerated
            ? Access.PasswordHashing.SameSecret(
                Access.GeneratedSetupCode.Normalize(input),
                Access.GeneratedSetupCode.Normalize(Code)
            )
            : Access.PasswordHashing.SameSecret(input?.Trim() ?? string.Empty, Code!.Trim());
    }
}
