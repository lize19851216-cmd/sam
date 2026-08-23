using SAM.Core.Steam;

namespace SAM.Infrastructure.Steam;

/// <summary>
/// Hosts one secret-free broker request. Interactive credential collection is
/// deliberately owned by the separately launched broker application.
/// </summary>
public sealed class SteamAuthenticationBrokerHost(ISteamAuthenticationTransport authenticationTransport)
{
    public Task ServeOnceAsync(string pipeName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authenticationTransport);
        return NamedPipeSteamAuthenticationBroker.ServeOnceAsync(pipeName, AuthenticateAsync, cancellationToken);
    }

    /// <summary>Continues serving local requests until the separately controlled broker is cancelled.</summary>
    public async Task ServeUntilCancelledAsync(string pipeName, CancellationToken cancellationToken = default)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
                await ServeOnceAsync(pipeName, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation is the normal shutdown path for the standalone broker.
        }
    }

    private async Task<SteamAuthenticationBrokerResponse> AuthenticateAsync(SteamAuthenticationBrokerRequest request, CancellationToken cancellationToken)
    {
        if (request.Kind == SteamAuthenticationBrokerRequestKind.Probe)
            return new SteamAuthenticationBrokerResponse(SteamAuthenticationStatus.Failed);

        try
        {
            var result = await authenticationTransport.AuthenticateAsync(request.AccountName, cancellationToken).ConfigureAwait(false);
            var response = result.Status == SteamAuthenticationStatus.Online
                ? new SteamAuthenticationBrokerResponse(result.Status, result.SteamId, result.PersonaName)
                : new SteamAuthenticationBrokerResponse(result.Status);
            response.Validate();
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new SteamAuthenticationBrokerResponse(SteamAuthenticationStatus.Failed);
        }
    }
}
