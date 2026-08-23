using System.IO.Pipes;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace SAM.PluginHost;

/// <summary>Metadata-only local IPC transport restricted to the current user. The caller must launch any restricted child process separately.</summary>
public sealed class NamedPipePluginIsolationHost : IIsolatedPluginHost
{
    private const int MaximumMessageSize = 1_048_576;
    private static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _pipeName;
    private readonly TimeSpan _operationTimeout;

    public NamedPipePluginIsolationHost(string pipeName, TimeSpan? operationTimeout = null)
    {
        _pipeName = PluginIsolationEndpoint.ValidatePipeName(pipeName);
        _operationTimeout = operationTimeout ?? DefaultOperationTimeout;
        if (_operationTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(operationTimeout), "The isolated plugin host operation timeout must be positive.");
    }

    public async Task<PluginIsolationResult> InspectAsync(PluginIsolationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_operationTimeout);
        await using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PluginIsolationEndpoint.LocalUserPipeOptions);
        await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
        await WriteMessageAsync(pipe, JsonSerializer.Serialize(request, JsonOptions), timeout.Token).ConfigureAwait(false);
        var response = await ReadMessageAsync(pipe, timeout.Token).ConfigureAwait(false);
        return JsonSerializer.Deserialize<PluginIsolationResult>(response, JsonOptions) ?? throw new InvalidDataException("Invalid isolated plugin host response.");
    }

    public static async Task ServeOnceAsync(string pipeName, Func<PluginIsolationRequest, CancellationToken, Task<PluginIsolationResult>> inspect, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inspect);
        pipeName = PluginIsolationEndpoint.ValidatePipeName(pipeName);
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PluginIsolationEndpoint.LocalUserPipeOptions);
        await pipe.WaitForConnectionAsync(cancellationToken);
        var line = await ReadMessageAsync(pipe, cancellationToken);
        var request = JsonSerializer.Deserialize<PluginIsolationRequest>(line, JsonOptions) ?? throw new InvalidDataException("Invalid isolated plugin host request.");
        request.Validate();
        await WriteMessageAsync(pipe, JsonSerializer.Serialize(await inspect(request, cancellationToken), JsonOptions), cancellationToken);
    }

    private static async Task WriteMessageAsync(Stream stream, string message, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(message);
        if (payload.Length is <= 0 or > MaximumMessageSize) throw new InvalidDataException("Invalid isolated plugin host message size.");
        var length = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);
        await stream.WriteAsync(length, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<string> ReadMessageAsync(Stream stream, CancellationToken cancellationToken)
    {
        var length = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(length, cancellationToken);
        var size = BinaryPrimitives.ReadInt32LittleEndian(length);
        if (size is <= 0 or > MaximumMessageSize) throw new InvalidDataException("Invalid isolated plugin host message size.");
        var payload = new byte[size];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return Encoding.UTF8.GetString(payload);
    }
}
