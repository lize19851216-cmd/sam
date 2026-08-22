using SAM.Core;
using SAM.Core.Steam;

namespace SAM.Infrastructure.Steam;
public sealed class SteamClientFactory
{
    public ISteamClientService Create(SteamClientMode mode, ISteamAuthenticationTransport? transport = null) => mode switch
    {
        SteamClientMode.Fake => new FakeSteamClient(),
        SteamClientMode.SteamKit when transport is not null => new SteamKitClientAdapter(transport),
        SteamClientMode.SteamKit => throw new InvalidOperationException("SteamKit mode requires an explicitly supplied transport."),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}
