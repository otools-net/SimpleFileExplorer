namespace Otools.Net.Sfe.Core.Data;

/// <summary>
/// Guests never get the rights that change anything (<see cref="Access.RightDefinition.GuestAllowed"/>).
/// </summary>
public enum UserKind
{
    User,
    Guest,
}
