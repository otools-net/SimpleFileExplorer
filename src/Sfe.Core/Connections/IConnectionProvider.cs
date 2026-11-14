using Otools.Net.Sfe.Core.Options;

namespace Otools.Net.Sfe.Core.Connections;

/// <summary>
/// One kind of storage outside the library (Dropbox, later WebDAV, SFTP…): what it is called, what it can do, how
/// an account signs in to it, and how its files are reached once signed in. One instance per kind, listed in
/// <see cref="ConnectionProviders"/>; adding a kind is writing one of these and its <see cref="IRemoteStorage"/>
/// (agents/docs/features/connection-providers.md).
/// </summary>
public interface IConnectionProvider
{
    /// <summary>The kind's id, stored with each connection and named in <c>Sfe:Connections:Providers</c>.</summary>
    string Kind { get; }

    /// <summary>The name people know it by.</summary>
    string Name { get; }

    /// <summary>The name of its icon in the web client.</summary>
    string Icon { get; }

    ProviderCapabilities Capabilities { get; }

    /// <summary>
    /// The ways to sign in that work with the server's settings as they are, the preferred one first; none when
    /// whoever runs the server has not configured the kind yet (Dropbox: no app key).
    /// </summary>
    IReadOnlyList<SignInMethod> SignInMethods(ConnectionOptions options);

    /// <summary>
    /// Starts a sign-in: for a provider asking nothing more the answer is the grant itself, else what to do next
    /// (an address to open, fields to fill) and what the server must keep until then.
    /// </summary>
    Task<SignInStep> BeginSignInAsync(SignInStart start, CancellationToken cancellationToken);

    /// <summary>Ends a sign-in begun by <see cref="BeginSignInAsync"/> with what the person brought back.</summary>
    Task<ConnectionGrant> CompleteSignInAsync(SignInCompletion completion, CancellationToken cancellationToken);

    /// <summary>
    /// The files of a signed-in account, seen from <paramref name="rootPath"/> (a folder of the remote storage,
    /// empty for all of it): every path given to the result is relative to it.
    /// </summary>
    IRemoteStorage Open(string credentials, string rootPath, ConnectionOptions options);

    /// <summary>
    /// Checks the kind's settings with the provider when it can (an app's key and secret), for the administrator's
    /// "Test" button; never throws for an answer of the provider.
    /// </summary>
    Task<ProviderTest> TestAsync(ConnectionOptions options, CancellationToken cancellationToken);
}
