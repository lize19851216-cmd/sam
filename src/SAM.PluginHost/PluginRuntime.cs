using SAM.Core.Plugins;

namespace SAM.PluginHost;

/// <summary>Owns plugins loaded during one host session and stops them deterministically.</summary>
public sealed class PluginRuntime(IEnumerable<ISamPlugin> plugins)
{
    private readonly IReadOnlyList<ISamPlugin> _plugins = plugins.ToArray();
    private bool _stopped;

    public IReadOnlyList<ISamPlugin> Plugins => _plugins;

    public PluginUnloadReport Stop()
    {
        if (_stopped) return new([], []);

        _stopped = true;
        var stopped = new List<string>();
        var failures = new List<PluginUnloadFailure>();
        foreach (var plugin in _plugins.Reverse())
        {
            var stoppedSuccessfully = true;
            try
            {
                plugin.Shutdown();
            }
            catch (Exception exception)
            {
                stoppedSuccessfully = false;
                failures.Add(new PluginUnloadFailure(plugin.Id, exception.Message));
            }

            try
            {
                if (plugin is IDisposable disposable) disposable.Dispose();
            }
            catch (Exception exception)
            {
                stoppedSuccessfully = false;
                failures.Add(new PluginUnloadFailure(plugin.Id, exception.Message));
            }

            if (stoppedSuccessfully) stopped.Add(plugin.Id);
        }

        return new(stopped, failures);
    }
}

public sealed record PluginUnloadFailure(string PluginId, string Message);
public sealed record PluginUnloadReport(IReadOnlyList<string> StoppedPluginIds, IReadOnlyList<PluginUnloadFailure> Failures);
