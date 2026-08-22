using SAM.Core.Steam;

namespace SAM.Infrastructure.Steam;

/// <summary>Safe runtime configuration. SteamKit is disabled unless explicitly enabled by the host.</summary>
public sealed record SteamClientOptions(SteamClientMode Mode = SteamClientMode.Fake, bool EnableSteamKit = false)
{
    public SteamClientMode EffectiveMode => EnableSteamKit ? Mode : SteamClientMode.Fake;
}
