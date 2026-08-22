using SAM.Core;
using SAM.Core.Steam;

namespace SAM.Infrastructure.Steam;

/// <summary>Maps a SteamKit transport's sanitized result to SAM's existing client contract.</summary>
public sealed class SteamKitClientAdapter(ISteamAuthenticationTransport transport) : ISteamClientService
{
    public async Task<LoginResult> LoginAsync(Account account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        var result = await transport.AuthenticateAsync(account.AccountName, cancellationToken);
        account.SteamId = result.SteamId ?? account.SteamId;
        account.PersonaName = result.PersonaName ?? account.PersonaName;
        return new(result.Status switch {
            SteamAuthenticationStatus.Online => AccountStatus.Online,
            SteamAuthenticationStatus.RequiresSteamGuard => AccountStatus.RequiresSteamGuard,
            SteamAuthenticationStatus.RateLimited => AccountStatus.RateLimited,
            _ => AccountStatus.Failed }, result.Message);
    }
}
