using Sfe.Core.Transfers;

namespace Sfe.Tests.Infrastructure;

/// <summary>
/// A transfer's source whose files never give a byte until the read is cancelled: a job is caught in the middle of a
/// file (its partial file made), everything else is the inner side's.
/// </summary>
public sealed class StallingSource(ITransferEndpoint inner) : ITransferEndpoint
{
    public string? HashName => inner.HashName;

    public Task<TransferEntry?> GetAsync(string path, CancellationToken cancellationToken) =>
        inner.GetAsync(path, cancellationToken);

    public IAsyncEnumerable<TransferEntry> ListAsync(string folder, CancellationToken cancellationToken) =>
        inner.ListAsync(folder, cancellationToken);

    public Task<Stream> OpenReadAsync(TransferEntry file, long offset, CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(new Stalled());

    public Task<ITransferWrite> BeginWriteAsync(
        string folder,
        string name,
        long size,
        DateTime? modified,
        CancellationToken cancellationToken
    ) => inner.BeginWriteAsync(folder, name, size, modified, cancellationToken);

    public Task<TransferEntry> CreateFolderAsync(string folder, string name, CancellationToken cancellationToken) =>
        inner.CreateFolderAsync(folder, name, cancellationToken);

    public Task<string> FreeNameAsync(string folder, string name, bool isFolder, CancellationToken cancellationToken) =>
        inner.FreeNameAsync(folder, name, isFolder, cancellationToken);

    public Task RemoveForReplaceAsync(TransferEntry existing, CancellationToken cancellationToken) =>
        inner.RemoveForReplaceAsync(existing, cancellationToken);

    public Task DeleteSourceAsync(TransferEntry entry, CancellationToken cancellationToken) =>
        inner.DeleteSourceAsync(entry, cancellationToken);

    public Task<TransferSpace> FreeSpaceAsync(string folder, CancellationToken cancellationToken) =>
        inner.FreeSpaceAsync(folder, cancellationToken);

    public Task AfterWriteAsync(TransferEntry written, CancellationToken cancellationToken) =>
        inner.AfterWriteAsync(written, cancellationToken);

    public ValueTask DisposeAsync() => inner.DisposeAsync();

    /// <summary>A stream whose reads wait until they are cancelled.</summary>
    private sealed class Stalled : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
