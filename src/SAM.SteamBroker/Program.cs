using SAM.Core.Steam;
using SAM.Infrastructure.Steam;
using SteamKit2;
using SteamKit2.Authentication;
using System.Runtime.InteropServices;

var pipeName = args.Length == 1 ? args[0] : "sam-steam-auth";
try
{
    pipeName = SteamAuthenticationBrokerEndpoint.ValidatePipeName(pipeName);
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

Console.WriteLine("SAM Steam authentication broker is waiting for local requests.");
Console.WriteLine("Credentials are requested only after a request arrives and are never written to disk.");
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    var configurator = new ConsoleSteamLogOnConfigurator();
    // Keep the desktop/broker boundary local and credential-free, but use the
    // maintained SteamKit sample's direct client/callback model inside this
    // user-controlled Broker process.
    var transport = new OfficialSteamKitBrokerTransport(configurator);
    var host = new SteamAuthenticationBrokerHost(transport);
    await host.ServeUntilCancelledAsync(pipeName, cancellation.Token);
    return 0;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.WriteLine("SAM Steam authentication broker was cancelled.");
    return 1;
}
catch
{
    Console.Error.WriteLine("SAM Steam authentication broker stopped unexpectedly.");
    return 1;
}

internal sealed class ConsoleSteamLogOnConfigurator
{
    public void Configure(AuthSessionDetails authSessionDetails)
    {
        Console.WriteLine("Enter credentials in this window only. They are masked, kept only in memory, and never written to disk.");
        Console.Write($"Password for {authSessionDetails.Username} (type now, then press Enter): ");
        authSessionDetails.Password = ReadSecret();
        // The host-owned factory supplies the interactive authenticator and
        // enforces non-persistence. This configurator may supply only the
        // short-lived password for the current request.
        authSessionDetails.IsPersistentSession = false;
    }

    private static string ReadSecret()
    {
        var value = new List<char>();
        while (Console.ReadKey(intercept: true) is var key && key.Key != ConsoleKey.Enter)
        {
            if (key.Key == ConsoleKey.Backspace)
            {
                if (value.Count > 0)
                {
                    value.RemoveAt(value.Count - 1);
                    Console.Write("\b \b");
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                value.Add(key.KeyChar);
                Console.Write('*');
            }
        }

        Console.WriteLine();
        try
        {
            return new string(CollectionsMarshal.AsSpan(value));
        }
        finally
        {
            CollectionsMarshal.AsSpan(value).Clear();
            value.Clear();
        }
    }
}

/// <summary>
/// Direct adaptation of SteamKit's maintained authentication sample. This
/// process is the only component that receives interactive credentials; it
/// keeps them in memory for one request and never retains guard or refresh
/// tokens after the authenticated logon callback completes.
/// </summary>
internal sealed class OfficialSteamKitBrokerTransport(ConsoleSteamLogOnConfigurator configurator) : ISteamAuthenticationTransport
{
    public async Task<SteamAuthenticationResult> AuthenticateAsync(string accountName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        ArgumentNullException.ThrowIfNull(configurator);

        var client = new SteamClient();
        var callbacks = new CallbackManager(client);
        var steamUser = client.GetHandler<SteamUser>() ?? throw new InvalidOperationException("SteamKit user handler is unavailable.");
        var completion = new TaskCompletionSource<SteamAuthenticationResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var connected = callbacks.Subscribe<SteamClient.ConnectedCallback>(callback =>
        {
            _ = BeginCredentialAuthenticationAsync(client, steamUser, accountName, completion, cancellationToken);
        });
        using var loggedOn = callbacks.Subscribe<SteamUser.LoggedOnCallback>(callback =>
            completion.TrySetResult(SteamKitAuthenticationResultMapper.From(callback.Result, callback.ClientSteamID?.ToString(), callback.ExtendedResult)));
        using var disconnected = callbacks.Subscribe<SteamClient.DisconnectedCallback>(_ =>
            completion.TrySetResult(new SteamAuthenticationResult(SteamAuthenticationStatus.Failed, "Steam connection was closed.")));
        using var cancellation = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));

        try
        {
            client.Connect();
            while (!completion.Task.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                callbacks.RunWaitCallbacks(TimeSpan.FromSeconds(1));
            }

            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            client.Disconnect();
        }
    }

    private async Task BeginCredentialAuthenticationAsync(
        SteamClient client,
        SteamUser steamUser,
        string accountName,
        TaskCompletionSource<SteamAuthenticationResult> completion,
        CancellationToken cancellationToken)
    {
        try
        {
            var authenticator = new UserConsoleAuthenticator();
            var details = new AuthSessionDetails
            {
                Username = accountName,
                IsPersistentSession = false,
                Authenticator = authenticator
            };
            configurator.Configure(details);

            if (!string.Equals(details.Username, accountName, StringComparison.Ordinal) ||
                details.IsPersistentSession ||
                !ReferenceEquals(details.Authenticator, authenticator))
                throw new InvalidOperationException("The local credential prompt changed a protected authentication setting.");

            cancellationToken.ThrowIfCancellationRequested();
            var authSession = await client.Authentication.BeginAuthSessionViaCredentialsAsync(details).ConfigureAwait(false);
            var pollResponse = await authSession.PollingWaitForResultAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.Equals(pollResponse.AccountName, accountName, StringComparison.Ordinal))
                throw new InvalidOperationException("Steam authentication returned an unexpected account name.");

            steamUser.LogOn(new SteamUser.LogOnDetails
            {
                Username = pollResponse.AccountName,
                AccessToken = pollResponse.RefreshToken,
                ShouldRememberPassword = false
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            completion.TrySetCanceled(cancellationToken);
        }
        catch (AuthenticationException exception)
        {
            completion.TrySetResult(SteamKitAuthenticationResultMapper.From(exception));
        }
        catch
        {
            completion.TrySetResult(new SteamAuthenticationResult(SteamAuthenticationStatus.Failed, "Steam authentication could not be completed."));
        }
    }
}
