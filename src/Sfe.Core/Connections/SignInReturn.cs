namespace Otools.Net.Sfe.Core.Connections;

/// <summary>Where the provider's redirect lands in the web client once a sign-in ends (never a sent address).</summary>
public enum SignInReturn
{
    /// <summary>The connections' landing page (<c>connections?made=</c>): a toast, the connection's page.</summary>
    Connections,

    /// <summary>The add-a-connection wizard (<c>wizard?id=add-connection&amp;made=</c>), which ends there.</summary>
    Wizard,
}
