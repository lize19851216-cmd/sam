namespace SAM.Core.Steam;

/// <summary>Sanitized outcome returned by a future SteamKit transport; it intentionally contains no credentials or secrets.</summary>
public enum SteamAuthenticationStatus { Online, RequiresSteamGuard, InvalidSteamGuardCode, InvalidCredentials, RateLimited, Failed }
public sealed record SteamAuthenticationResult(SteamAuthenticationStatus Status, string Message, string? SteamId = null, string? PersonaName = null);

/// <summary>Secret-free request sent from SAM to a separately controlled local authentication broker.</summary>
public enum SteamAuthenticationBrokerRequestKind { Authenticate, Probe }

/// <summary>Secret-free request sent from SAM to a separately controlled local authentication broker.</summary>
public sealed record SteamAuthenticationBrokerRequest(string AccountName, SteamAuthenticationBrokerRequestKind Kind = SteamAuthenticationBrokerRequestKind.Authenticate)
{
    public const int MaximumAccountNameLength = 64;

    public void Validate()
    {
        if (!Enum.IsDefined(Kind))
            throw new ArgumentOutOfRangeException(nameof(Kind));
        if (Kind == SteamAuthenticationBrokerRequestKind.Probe)
        {
            if (!string.IsNullOrEmpty(AccountName))
                throw new ArgumentException("A broker probe must not include an account name.", nameof(AccountName));
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(AccountName);
        if (AccountName.Length > MaximumAccountNameLength || AccountName.Any(char.IsControl))
            throw new ArgumentException("The account name is invalid.", nameof(AccountName));
        if (AccountName.StartsWith("mock_", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Simulated accounts cannot use the external authentication broker.", nameof(AccountName));
    }
}

/// <summary>Secret-free response returned by a separately controlled local authentication broker.</summary>
public sealed record SteamAuthenticationBrokerResponse(SteamAuthenticationStatus Status, string? SteamId = null, string? PersonaName = null)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Status))
            throw new ArgumentOutOfRangeException(nameof(Status));
        if (SteamId is { Length: > 20 } || SteamId is not null && !ulong.TryParse(SteamId, out _))
            throw new ArgumentException("The Steam ID is invalid.", nameof(SteamId));
        if (PersonaName is { Length: > 128 } || PersonaName?.Any(char.IsControl) is true)
            throw new ArgumentException("The persona name is invalid.", nameof(PersonaName));
    }
}

/// <summary>Low-level boundary for a SteamKit-backed transport. Implementations must never persist credentials.</summary>
public interface ISteamAuthenticationTransport
{
    Task<SteamAuthenticationResult> AuthenticateAsync(string accountName, CancellationToken cancellationToken);
}
