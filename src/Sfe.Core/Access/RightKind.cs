namespace Sfe.Core.Access;

/// <summary>
/// What a right does: only looks (<see cref="Read"/>), or changes something through one of its routes.
/// </summary>
public enum RightKind
{
    Read,
    Write,
}
