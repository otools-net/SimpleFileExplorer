namespace Otools.Net.Sfe.Core.Files;

/// <summary>
/// The disk refused to let the server's account read a file or folder of the library (not to change one): told as
/// such by <see cref="FileOperations.DiskFailure"/>. <see cref="Inside"/> is the library path of the item refused when
/// it is inside the one acted on (a file of a copied folder), null when it is that one.
/// </summary>
public sealed class UnreadableException(string? inside, Exception inner)
    : UnauthorizedAccessException(inner.Message, inner)
{
    public string? Inside { get; } = inside;
}
