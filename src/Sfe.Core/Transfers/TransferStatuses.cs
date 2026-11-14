using Sfe.Core.Connections;
using Sfe.Core.Files;

namespace Sfe.Core.Transfers;

/// <summary>The status of a transfer's item from the error of either side.</summary>
internal static class TransferStatuses
{
    public static FileOperationStatus Of(RemoteErrorKind kind) =>
        kind switch
        {
            RemoteErrorKind.NotFound => FileOperationStatus.NotFound,
            RemoteErrorKind.Exists => FileOperationStatus.Exists,
            RemoteErrorKind.InvalidName => FileOperationStatus.Invalid,
            RemoteErrorKind.Refused => FileOperationStatus.Forbidden,
            _ => FileOperationStatus.Failed,
        };
}
