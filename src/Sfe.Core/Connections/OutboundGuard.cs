namespace Otools.Net.Sfe.Core.Connections;

/// <summary>
/// Refuses a request the <see cref="OutboundRule"/> does not allow before it leaves, and gives up on a provider
/// that does not start answering in time (<see cref="OutboundPolicy.AnswerTimeout"/>).
/// </summary>
internal sealed class OutboundGuard(OutboundRule rule, HttpMessageHandler transport) : DelegatingHandler(transport)
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        if (request.RequestUri is not { } address || !rule.Allows(address))
        {
            // The host only: a path or a query may hold a token.
            throw new RemoteStorageException(
                RemoteErrorKind.Refused,
                $"The server does not call '{request.RequestUri?.Host}' for this kind of connection."
            );
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(OutboundPolicy.AnswerTimeout);
        try
        {
            return await base.SendAsync(request, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RemoteStorageException(RemoteErrorKind.Unavailable, "The provider did not answer in time.");
        }
    }
}
