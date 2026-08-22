using SAM.Core.Plugins;

namespace SAM.PluginHost;

public sealed record PluginLoadFailure(string AssemblyPath, string Message);
public sealed record PluginLoadReport(IReadOnlyList<ISamPlugin> Plugins, IReadOnlyList<PluginLoadFailure> Failures);

/// <summary>Validates plugin identity before exposing plugins to the host.</summary>
public sealed class PluginRegistry
{
    private readonly Dictionary<string, ISamPlugin> _plugins = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<ISamPlugin> Plugins => _plugins.Values;

    public void Register(ISamPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        if (string.IsNullOrWhiteSpace(plugin.Id)) throw new ArgumentException("A plugin Id is required.", nameof(plugin));
        if (_plugins.ContainsKey(plugin.Id)) throw new InvalidOperationException($"Plugin '{plugin.Id}' is already registered.");
        _plugins.Add(plugin.Id, plugin);
    }
}
