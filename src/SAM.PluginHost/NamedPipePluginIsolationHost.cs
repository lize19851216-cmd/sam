using System.IO.Pipes;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace SAM.PluginHost;

/// <summary>Metadata-only local IPC transport restricted to the current user. The caller must launch any restricted child process separately.</summary>
public sealed class NamedPipePluginIsolationHost : IIsolatedPluginHost
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _pipeName;

    public NamedPipePluginIsolationHost(string pipeName) => _pipeName = PluginIsolationEndpoint.ValidatePipeName(pipeName);

    public async Task<PluginIsolationResult> InspectAsync(PluginIsolationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        await using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PluginIsolationEndpoint.LocalUserPipeOptions);
        await pipe.ConnectAsync(cancellationToken);
        await WriteMessageAsync(pipe, JsonSerializer.Serialize(request, JsonOptions), cancellationToken);
        var response = await ReadMessageAsync(pipe, cancellationToken);
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
        if (size is <= 0 or > 1_048_576) throw new InvalidDataException("Invalid isolated plugin host message size.");
        var payload = new byte[size];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return Encoding.UTF8.GetString(payload);
    }
}
