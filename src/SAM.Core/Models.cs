namespace SAM.Core;

public enum AccountStatus
{
    Imported, Queued, Connecting, Authenticating, Online,
    RequiresSteamGuard, RateLimited, RetryWaiting, Failed, Cancelled, Disabled
}

public sealed class Account
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string AccountName { get; init; } = "";
    public string SteamId { get; set; } = "";
    public string PersonaName { get; set; } = "";
    public AccountStatus Status { get; set; } = AccountStatus.Imported;
    public int RetryCount { get; set; }
    public string LastMessage { get; set; } = "";
}

public sealed record LoginResult(AccountStatus Status, string Message);

public interface ISteamClientService
{
    Task<LoginResult> LoginAsync(Account account, CancellationToken cancellationToken);
}
