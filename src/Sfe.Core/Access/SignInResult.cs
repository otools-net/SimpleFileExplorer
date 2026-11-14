namespace Otools.Net.Sfe.Core.Access;

public sealed record SignInResult(SignInStatus Status, SessionUser? User = null, DateTime? LockedUntil = null);
