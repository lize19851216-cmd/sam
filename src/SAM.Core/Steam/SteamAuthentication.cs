namespace SAM.Core.Steam;

/// <summary>Sanitized outcome returned by a future SteamKit transport; it intentionally contains no credentials or secrets.</summary>
public enum SteamAuthenticationStatus { Online, RequiresSteamGuard, RateLimited, Failed }
public sealed record SteamAuthenticationResult(SteamAuthenticationStatus Status, string Message, string? SteamId = null, string? PersonaName = null);

/// <summary>Low-level boundary for a SteamKit-backed transport. Implementations must never persist credentials.</summary>
public interface ISteamAuthenticationTransport
{
    Task<SteamAuthenticationResult> AuthenticateAsync(string accountName, CancellationToken cancellationToken);
}
