using Sfe.Api.Security;

namespace Sfe.Api.Maintenance;

/// <param name="SetupDone">The server has its administrator: the mode no longer goes back from External to Local
/// here.</param>
/// <param name="OnThisComputer">The visitor is on the server's own computer (<see cref="LocalVisitor"/>).</param>
public sealed record InstallationVisitorInfo(bool SetupDone, bool OnThisComputer);
