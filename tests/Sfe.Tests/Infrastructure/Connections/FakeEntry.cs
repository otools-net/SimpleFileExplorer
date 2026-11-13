namespace Sfe.Tests;

/// <summary>
/// A file (with its bytes) or a folder (<paramref name="Content"/> null) of a <see cref="FakeAccount"/>.
/// </summary>
/// <param name="Path">From the account's root, as first spelled.</param>
public sealed record FakeEntry(string Path, byte[]? Content, DateTime? Modified, string Id)
{
    public bool IsFolder => Content is null;
}
