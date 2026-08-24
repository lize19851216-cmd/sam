using SAM.Core.Steam;
using SAM.Infrastructure.Steam;
using QRCoder;
using SteamKit2;
using SteamKit2.Authentication;

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
Console.WriteLine("QR sign-in is requested only after a local request arrives; no password, code, or token is written to disk.");
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    // QR-first is the mature SteamKit authentication path used by established
    // clients. The desktop/broker boundary remains local and credential-free.
    var transport = new QrFirstSteamKitBrokerTransport();
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

/// <summary>
/// QR-first adaptation of the maintained SteamKit and DepotDownloader flow.
/// A Steam Mobile App confirmation replaces password and Guard-code entry.
/// The refresh token exists only long enough to complete this one logon.
/// </summary>
internal sealed class QrFirstSteamKitBrokerTransport : ISteamAuthenticationTransport
{
    public async Task<SteamAuthenticationResult> AuthenticateAsync(string accountName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);

        var client = new SteamClient();
        var callbacks = new CallbackManager(client);
        var steamUser = client.GetHandler<SteamUser>() ?? throw new InvalidOperationException("SteamKit user handler is unavailable.");
        var completion = new TaskCompletionSource<SteamAuthenticationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var authenticationStarted = false;

        void StartQrAuthentication()
        {
            if (authenticationStarted)
                return;

            authenticationStarted = true;
            _ = BeginQrAuthenticationAsync(client, steamUser, completion, cancellationToken);
        }

        using var connected = callbacks.Subscribe<SteamClient.ConnectedCallback>(_ => StartQrAuthentication());
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

    private static async Task BeginQrAuthenticationAsync(
        SteamClient client,
        SteamUser steamUser,
        TaskCompletionSource<SteamAuthenticationResult> completion,
        CancellationToken cancellationToken)
    {
        try
        {
            var session = await client.Authentication.BeginAuthSessionViaQRAsync(new AuthSessionDetails
            {
                DeviceFriendlyName = "SAM",
                IsPersistentSession = false
            }).ConfigureAwait(false);
            session.ChallengeURLChanged = () => SteamQrConsole.Write(session.ChallengeURL);
            SteamQrConsole.Write(session.ChallengeURL);

            cancellationToken.ThrowIfCancellationRequested();
            var pollResponse = await session.PollingWaitForResultAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

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

internal static class SteamQrConsole
{
    public static void Write(string challengeUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(challengeUrl);
        Console.WriteLine();
        Console.WriteLine("Use Steam Mobile App to scan this one-time sign-in QR code and approve the request:");
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(challengeUrl, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new AsciiQRCode(data);
        Console.WriteLine(qrCode.GetGraphic(1, drawQuietZones: true));
        Console.WriteLine("Waiting for approval. The QR code may refresh automatically.");
    }
}
