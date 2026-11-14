using Otools.Net.Sfe.Core.Options;

namespace Otools.Net.Sfe.Core.Connections;

/// <summary>How an account signs in to a provider.</summary>
public enum SignInKind
{
    /// <summary>The browser goes to the provider and comes back to SFE with a code (the server's address must be
    /// registered at the provider).</summary>
    OAuthRedirect,

    /// <summary>The browser goes to the provider, which shows a code the person copies into SFE.</summary>
    OAuthCode,

    /// <summary>An address, a name and a password typed into SFE.</summary>
    Password,

    /// <summary>An address, a name and a private key.</summary>
    Key,
}

public enum SignInFieldType
{
    Text,

    /// <summary>Never shown again nor sent back once typed.</summary>
    Secret,

    /// <summary>A server address typed by a person: checked by <see cref="OutboundPolicy"/> before any call.</summary>
    Address,
}

/// <summary>
/// Something the person fills to sign in; described as data so the client's form needs no code per kind.
/// </summary>
public sealed record SignInField(string Id, string Label, SignInFieldType Type = SignInFieldType.Text);

/// <param name="Fields">Asked before the sign-in starts (none for OAuth).</param>
public sealed record SignInMethod(SignInKind Kind, IReadOnlyList<SignInField> Fields);

/// <summary>What <see cref="IConnectionProvider.BeginSignInAsync"/> is given.</summary>
/// <param name="ReturnAddress">For <see cref="SignInKind.OAuthRedirect"/>: where the provider sends the browser
/// back, exactly as registered there.</param>
/// <param name="State">Travels to the provider and back unchanged; the server recognises the sign-in by it.</param>
/// <param name="Fields">The method's fields as filled, by id.</param>
public sealed record SignInStart(
    SignInKind Kind,
    ConnectionOptions Options,
    string? ReturnAddress,
    string State,
    IReadOnlyDictionary<string, string> Fields
);

/// <summary>
/// The answer to a sign-in started: done (<paramref name="Grant"/>), or waiting for the person.
/// </summary>
/// <param name="Address">Where the browser must go (the provider's own page); null when only fields are asked.</param>
/// <param name="Pending">What the server keeps, protected, until the person comes back (a PKCE verifier): never
/// sent to the browser.</param>
/// <param name="Fields">What the person brings back (the code shown by the provider).</param>
public sealed record SignInStep(
    ConnectionGrant? Grant,
    string? Address = null,
    string? Pending = null,
    IReadOnlyList<SignInField>? Fields = null
);

/// <summary>What <see cref="IConnectionProvider.CompleteSignInAsync"/> is given.</summary>
/// <param name="Pending">The <see cref="SignInStep.Pending"/> of the start.</param>
/// <param name="ReturnAddress">The same as at the start (providers check it again).</param>
/// <param name="Fields">What the person brought back, by id (<c>code</c>).</param>
public sealed record SignInCompletion(
    SignInKind Kind,
    ConnectionOptions Options,
    string? Pending,
    string? ReturnAddress,
    IReadOnlyDictionary<string, string> Fields
);

/// <summary>A sign-in that worked.</summary>
/// <param name="Credentials">What <see cref="IConnectionProvider.Open"/> needs later (a refresh token, as the
/// provider's own text): stored protected, never shown.</param>
/// <param name="Account">Whose storage it is, to show (an e-mail address, a name).</param>
/// <param name="AccountId">The provider's id of that account, to tell a reconnection to another account.</param>
public sealed record ConnectionGrant(string Credentials, string Account, string? AccountId = null);
