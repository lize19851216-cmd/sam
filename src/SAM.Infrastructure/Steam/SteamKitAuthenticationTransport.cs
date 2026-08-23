using SAM.Core.Steam;
using SteamKit2;

namespace SAM.Infrastructure.Steam;

/// <summary>
/// Supplies one interactive SteamKit logon request. Implementations may obtain
/// credentials from a user-controlled source, but must not persist them.
/// </summary>
public interface IExternalSteamLogOnConfigurator
{
    void Configure(SteamUser.LogOnDetails logOnDetails);
}

/// <summary>Creates a short-lived SteamKit session for one authentication attempt.</summary>
public interface ISteamKitAuthenticationSessionFactory
{
    ISteamKitAuthenticationSession Create();
}

/// <summary>A disposable, single-use SteamKit authentication session.</summary>
public interface ISteamKitAuthenticationSession : IAsyncDisposable
{
    Task<SteamAuthenticationResult> AuthenticateAsync(string accountName, CancellationToken cancellationToken);
}

/// <summary>
/// Concrete transport bridge for SteamKit. It retains no credentials; the
/// caller must explicitly provide an external, non-persisting configurator.
/// </summary>
public sealed class SteamKitAuthenticationTransport(ISteamKitAuthenticationSessionFactory sessionFactory) : ISteamAuthenticationTransport
{
    public async Task<SteamAuthenticationResult> AuthenticateAsync(string accountName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        ArgumentNullException.ThrowIfNull(sessionFactory);

        try
        {
            await using var session = sessionFactory.Create() ?? throw new InvalidOperationException("SteamKit session factory returned no session.");
            return await session.AuthenticateAsync(accountName, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new SteamAuthenticationResult(SteamAuthenticationStatus.Failed, "Steam authentication could not be completed.");
        }
    }
}

/// <summary>Creates short-lived sessions backed by the SteamKit2 NuGet package.</summary>
public sealed class SteamKitAuthenticationSessionFactory(IExternalSteamLogOnConfigurator configurator) : ISteamKitAuthenticationSessionFactory
{
    public ISteamKitAuthenticationSession Create()
    {
        ArgumentNullException.ThrowIfNull(configurator);
        return new SteamKitAuthenticationSession(configurator);
    }
}

/// <summary>
/// Builds the logon details immediately before sending them to SteamKit. The
/// account name is host-owned and password remembering is always disabled.
/// </summary>
public static class SteamKitLogOnDetailsFactory
{
    public static SteamUser.LogOnDetails Create(string accountName, IExternalSteamLogOnConfigurator configurator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        ArgumentNullException.ThrowIfNull(configurator);

        var details = new SteamUser.LogOnDetails { Username = accountName, ShouldRememberPassword = false };
        configurator.Configure(details);

        if (!string.Equals(details.Username, accountName, StringComparison.Ordinal))
            throw new InvalidOperationException("The external Steam credential source cannot change the account name.");

        details.ShouldRememberPassword = false;
        return details;
    }
}

/// <summary>
/// Applies a user-entered Steam Guard code without retaining it. Steam uses
/// separate protocol fields for email codes and mobile-authenticator codes;
/// providing the same short-lived code in both fields lets the server consume
/// the field appropriate for the account's configured guard method.
/// </summary>
public static class SteamKitGuardCodeConfigurator
{
    public static void Apply(SteamUser.LogOnDetails details, string code)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        details.AuthCode = code;
        details.TwoFactorCode = code;
    }
}

/// <summary>Maps SteamKit protocol responses to SAM's credential-free transport result.</summary>
public static class SteamKitAuthenticationResultMapper
{
    public static SteamAuthenticationResult From(EResult result, string? steamId = null) => result switch
    {
        EResult.OK => new(SteamAuthenticationStatus.Online, "Steam authentication succeeded.", steamId),
        _ when IsInvalidSteamGuardCodeResult(result) => new(SteamAuthenticationStatus.InvalidSteamGuardCode, "Steam Guard code was rejected or expired."),
        _ when IsSteamGuardResult(result) => new(SteamAuthenticationStatus.RequiresSteamGuard, "Steam Guard verification is required."),
        _ when IsInvalidCredentialResult(result) => new(SteamAuthenticationStatus.InvalidCredentials, "Steam rejected the account name or password."),
        _ when IsRateLimitedResult(result) => new(SteamAuthenticationStatus.RateLimited, "Steam temporarily limited this authentication attempt."),
        _ => new(SteamAuthenticationStatus.Failed, "Steam authentication was rejected.")
    };

    private static bool IsInvalidSteamGuardCodeResult(EResult result) => result is EResult.InvalidLoginAuthCode or EResult.ExpiredLoginAuthCode;
    private static bool IsSteamGuardResult(EResult result) => result.ToString() is "AccountLogonDenied" or "AccountLogonDeniedNoMail" or "AccountLoginDeniedNeedTwoFactor";
    private static bool IsInvalidCredentialResult(EResult result) => result is EResult.InvalidPassword or EResult.AccountNotFound or EResult.IllegalPassword or EResult.PasswordUnset or EResult.RequirePasswordReEntry;
    private static bool IsRateLimitedResult(EResult result) => result.ToString() is "RateLimitExceeded" or "ServiceUnavailable" or "TryAnotherCM";
}

internal sealed class SteamKitAuthenticationSession : ISteamKitAuthenticationSession
{
    private readonly IExternalSteamLogOnConfigurator _configurator;
    private readonly SteamClient _client = new();
    private readonly CallbackManager _callbacks;
    private int _authenticationStarted;

    public SteamKitAuthenticationSession(IExternalSteamLogOnConfigurator configurator)
    {
        _configurator = configurator ?? throw new ArgumentNullException(nameof(configurator));
        _callbacks = new CallbackManager(_client);
    }

    public async Task<SteamAuthenticationResult> AuthenticateAsync(string accountName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        if (Interlocked.Exchange(ref _authenticationStarted, 1) != 0)
            throw new InvalidOperationException("A SteamKit authentication session can only be used once.");

        var completion = new TaskCompletionSource<SteamAuthenticationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var connected = _callbacks.Subscribe<SteamClient.ConnectedCallback>(callback =>
        {
            try
            {
                var steamUser = _client.GetHandler<SteamUser>() ?? throw new InvalidOperationException("SteamKit user handler is unavailable.");
                steamUser.LogOn(SteamKitLogOnDetailsFactory.Create(accountName, _configurator));
            }
            catch
            {
                completion.TrySetResult(new SteamAuthenticationResult(SteamAuthenticationStatus.Failed, "Steam authentication could not be started."));
            }
        });
        using var loggedOn = _callbacks.Subscribe<SteamUser.LoggedOnCallback>(callback =>
            completion.TrySetResult(SteamKitAuthenticationResultMapper.From(callback.Result, callback.ClientSteamID?.ToString())));
        using var disconnected = _callbacks.Subscribe<SteamClient.DisconnectedCallback>(_ =>
            completion.TrySetResult(new SteamAuthenticationResult(SteamAuthenticationStatus.Failed, "Steam connection was closed.")));
        using var cancellation = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));

        _client.Connect();
        while (!completion.Task.IsCompleted)
            await _callbacks.RunWaitCallbackAsync(cancellationToken).ConfigureAwait(false);

        return await completion.Task.ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        _client.Disconnect();
        return ValueTask.CompletedTask;
    }
}
