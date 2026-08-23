using SAM.Core;
using SAM.Core.Steam;

namespace SAM.Infrastructure.Steam;
public sealed class SteamClientFactory
{
    public ISteamClientService Create(SteamClientOptions options, ISteamAuthenticationTransport? transport = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Create(options.EffectiveMode, transport);
    }

    public ISteamClientService Create(SteamClientMode mode, ISteamAuthenticationTransport? transport = null) => mode switch
    {
        SteamClientMode.Fake => new FakeSteamClient(),
        SteamClientMode.SteamKit when transport is not null => new SteamKitClientAdapter(transport),
        SteamClientMode.SteamKit => throw new InvalidOperationException("SteamKit mode requires an explicitly supplied transport."),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    /// <summary>Creates an explicitly enabled SteamKit client that delegates authentication to a local external broker.</summary>
    public ISteamClientService CreateWithExternalBroker(SteamClientOptions options, string pipeName)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.EffectiveMode != SteamClientMode.SteamKit)
            throw new InvalidOperationException("External Steam authentication broker use must be explicitly enabled.");

        return Create(options, new NamedPipeSteamAuthenticationBroker(pipeName));
    }
}
