namespace Otools.Net.Sfe.Core.Access;

/// <param name="UserName">The account the code recovers.</param>
/// <param name="Code">Shown only where the server runs (its console and <see cref="AccountRecovery.FilePath"/>), never
/// to the browser.</param>
public sealed record RecoveryCode(int UserId, string UserName, string Code, DateTime Expires);
