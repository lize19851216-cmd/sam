namespace SAM.PluginHost;

/// <summary>Restricted data contract for a future out-of-process plugin host. It carries metadata only, never plugin objects or host services.</summary>
public sealed record PluginIsolationRequest(string AssemblyPath, string ExpectedAssemblyHash)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AssemblyPath)) throw new ArgumentException("An assembly path is required.", nameof(AssemblyPath));
        if (ExpectedAssemblyHash.Length != 64 || !ExpectedAssemblyHash.All(Uri.IsHexDigit))
            throw new ArgumentException("A SHA-256 assembly hash is required.", nameof(ExpectedAssemblyHash));
    }
}

public sealed record PluginIsolationResult(bool Accepted, string Message, IReadOnlyList<PluginMetadata> Plugins)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Message);
        ArgumentNullException.ThrowIfNull(Plugins);
        foreach (var plugin in Plugins)
        {
            ArgumentNullException.ThrowIfNull(plugin);
            plugin.Validate();
        }
    }
}

public sealed record PluginMetadata(string Id, string Name, string Version)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(Version);
    }
}

/// <summary>Boundary for a future restricted IPC implementation. The in-process loader does not implement this contract.</summary>
public interface IIsolatedPluginHost
{
    Task<PluginIsolationResult> InspectAsync(PluginIsolationRequest request, CancellationToken cancellationToken = default);
}
