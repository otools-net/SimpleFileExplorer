namespace Sfe.Core.Transfers;

/// <summary>
/// The content hashes of one file computed while it streams through a transfer (<see cref="ContentHashes"/>): the
/// source's kind when the source told the file's hash, the target's kind when the target gives one; one hash when
/// both name the same kind. Neither: nothing is computed and only the size is verified.
/// </summary>
internal sealed class TransferHashes : IDisposable
{
    private readonly IncrementalContentHash? _source;
    private readonly IncrementalContentHash? _target;
    private readonly string? _expected;
    private string? _sourceHash;
    private string? _targetHash;

    public TransferHashes(TransferEntry file, string? sourceName, string? targetName)
    {
        if (file.Hash is not null)
        {
            _source = ContentHashes.Create(sourceName);
            _expected = _source is null ? null : file.Hash;
        }
        var same = _source is not null && string.Equals(sourceName, targetName, StringComparison.OrdinalIgnoreCase);
        _target = same ? null : ContentHashes.Create(targetName);
        SharesHash = same;
    }

    /// <summary>The target's hash is the source's kind: the one computed serves both.</summary>
    private bool SharesHash { get; }

    public void Append(ReadOnlySpan<byte> data)
    {
        _source?.Append(data);
        _target?.Append(data);
    }

    /// <summary>
    /// The bytes read differ from the hash the source told for the file (checked before the file is committed);
    /// null when they agree or the source told none.
    /// </summary>
    public string? SourceMismatch()
    {
        _sourceHash ??= _source?.Finish();
        return _expected is not null && !Same(_sourceHash, _expected)
            ? "what was read differs from the source's own hash"
            : null;
    }

    /// <summary>
    /// The target's hash of the written file differs from the bytes sent; null when they agree or no hash of the
    /// target's kind was computed.
    /// </summary>
    public string? TargetMismatch(TransferEntry written)
    {
        _sourceHash ??= _source?.Finish();
        _targetHash ??= _target?.Finish();
        var computed = SharesHash ? _sourceHash : _targetHash;
        return written.Hash is not null && computed is not null && !Same(computed, written.Hash)
            ? "what arrived differs from what was sent"
            : null;
    }

    public void Dispose()
    {
        _source?.Dispose();
        _target?.Dispose();
    }

    private static bool Same(string? computed, string told) =>
        string.Equals(computed, told, StringComparison.OrdinalIgnoreCase);
}
