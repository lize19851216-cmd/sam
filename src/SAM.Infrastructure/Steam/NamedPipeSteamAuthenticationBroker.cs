using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using SAM.Core.Steam;

namespace SAM.Infrastructure.Steam;

/// <summary>Validates and secures the secret-free, local Steam authentication broker endpoint.</summary>
public static class SteamAuthenticationBrokerEndpoint
{
    public const int MaximumPipeNameLength = 128;
    public static PipeOptions LocalUserPipeOptions => PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;

    public static string ValidatePipeName(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        if (pipeName.Length > MaximumPipeNameLength)
            throw new ArgumentOutOfRangeException(nameof(pipeName), $"Pipe names cannot exceed {MaximumPipeNameLength} characters.");
        if (pipeName.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            throw new ArgumentException("Pipe names may contain only ASCII letters, digits, hyphens, and underscores.", nameof(pipeName));

        return pipeName;
    }
}

/// <summary>
/// Connects to an externally controlled, current-user-only authentication broker.
/// The protocol carries account names and sanitized outcomes only; it never carries credentials or Steam Guard data.
/// </summary>
public sealed class NamedPipeSteamAuthenticationBroker : ISteamAuthenticationTransport
{
    private const int MaximumMessageSize = 4_096;
    public static readonly TimeSpan DefaultAuthenticationTimeout = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _pipeName;
    private readonly TimeSpan _authenticationTimeout;
    private readonly TimeSpan _probeTimeout;

    public NamedPipeSteamAuthenticationBroker(string pipeName, TimeSpan? operationTimeout = null)
    {
        _pipeName = SteamAuthenticationBrokerEndpoint.ValidatePipeName(pipeName);
        _authenticationTimeout = operationTimeout ?? DefaultAuthenticationTimeout;
        _probeTimeout = operationTimeout ?? DefaultProbeTimeout;
        if (_authenticationTimeout <= TimeSpan.Zero || _probeTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(operationTimeout), "The broker operation timeout must be positive.");
    }

    public async Task<SteamAuthenticationResult> AuthenticateAsync(string accountName, CancellationToken cancellationToken)
    {
        var request = new SteamAuthenticationBrokerRequest(accountName);
        request.Validate();

        try
        {
            return ToSanitizedResult(await ExchangeAsync(request, _authenticationTimeout, cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new SteamAuthenticationResult(SteamAuthenticationStatus.Failed, "Steam authentication broker is unavailable.");
        }
    }

    /// <summary>Checks whether a local broker is reachable without submitting an account or requesting credentials.</summary>
    public async Task<bool> ProbeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await ExchangeAsync(new SteamAuthenticationBrokerRequest(string.Empty, SteamAuthenticationBrokerRequestKind.Probe), _probeTimeout, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Provides a testable reference server for an externally hosted broker; no credentials are part of this protocol.</summary>
    public static async Task ServeOnceAsync(string pipeName, Func<SteamAuthenticationBrokerRequest, CancellationToken, Task<SteamAuthenticationBrokerResponse>> authenticate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authenticate);
        pipeName = SteamAuthenticationBrokerEndpoint.ValidatePipeName(pipeName);
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, SteamAuthenticationBrokerEndpoint.LocalUserPipeOptions);
        await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
        var request = JsonSerializer.Deserialize<SteamAuthenticationBrokerRequest>(await ReadMessageAsync(pipe, cancellationToken).ConfigureAwait(false), JsonOptions)
            ?? throw new InvalidDataException("Invalid Steam authentication broker request.");
        request.Validate();
        var response = await authenticate(request, cancellationToken).ConfigureAwait(false);
        response.Validate();
        await WriteMessageAsync(pipe, JsonSerializer.Serialize(response, JsonOptions), cancellationToken).ConfigureAwait(false);
    }

    private static SteamAuthenticationResult ToSanitizedResult(SteamAuthenticationBrokerResponse response) => response.Status switch
    {
        SteamAuthenticationStatus.Online => new(SteamAuthenticationStatus.Online, "Steam authentication succeeded.", response.SteamId, response.PersonaName),
        SteamAuthenticationStatus.RequiresSteamGuard => new(SteamAuthenticationStatus.RequiresSteamGuard, "Steam Guard verification is required."),
        SteamAuthenticationStatus.InvalidSteamGuardCode => new(SteamAuthenticationStatus.InvalidSteamGuardCode, "Steam Guard code was rejected or expired."),
        SteamAuthenticationStatus.InvalidCredentials => new(SteamAuthenticationStatus.InvalidCredentials, "Steam rejected the account name or password."),
        SteamAuthenticationStatus.RateLimited => new(SteamAuthenticationStatus.RateLimited, "Steam temporarily limited this authentication attempt."),
        _ => new(SteamAuthenticationStatus.Failed, "Steam authentication was rejected.")
    };

    private async Task<SteamAuthenticationBrokerResponse> ExchangeAsync(SteamAuthenticationBrokerRequest request, TimeSpan operationTimeout, CancellationToken cancellationToken)
    {
        request.Validate();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(operationTimeout);
        await using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, SteamAuthenticationBrokerEndpoint.LocalUserPipeOptions);
        await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
        await WriteMessageAsync(pipe, JsonSerializer.Serialize(request, JsonOptions), timeout.Token).ConfigureAwait(false);
        var response = JsonSerializer.Deserialize<SteamAuthenticationBrokerResponse>(await ReadMessageAsync(pipe, timeout.Token).ConfigureAwait(false), JsonOptions)
            ?? throw new InvalidDataException("Invalid Steam authentication broker response.");
        response.Validate();
        return response;
    }

    private static async Task WriteMessageAsync(Stream stream, string message, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(message);
        if (payload.Length is <= 0 or > MaximumMessageSize) throw new InvalidDataException("Steam authentication broker message size is invalid.");
        var length = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);
        await stream.WriteAsync(length, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReadMessageAsync(Stream stream, CancellationToken cancellationToken)
    {
        var length = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(length, cancellationToken).ConfigureAwait(false);
        var size = BinaryPrimitives.ReadInt32LittleEndian(length);
        if (size is <= 0 or > MaximumMessageSize) throw new InvalidDataException("Steam authentication broker message size is invalid.");
        var payload = new byte[size];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(payload);
    }
}
