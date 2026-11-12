namespace Sfe.Core.Access;

/// <summary>
/// What a folder lets a subject do in it, ordered: <see cref="Write"/> includes <see cref="Read"/>. Read means the
/// folder's files may be seen and used (tagged too, which changes the index, not the files); Write that its files on
/// disk may be changed.
/// </summary>
public enum FolderLevel
{
    Hidden,
    Read,
    Write,
}
