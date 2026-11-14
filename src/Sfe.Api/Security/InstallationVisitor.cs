namespace Otools.Net.Sfe.Api.Security;

/// <summary>
/// Whether the visitor may use the installation page (<see cref="InstallationAccess.CheckVisitorAsync"/>).
/// </summary>
public enum InstallationVisitor
{
    Allowed,
    NotSignedIn,
    NotServerAdmin,
}
