using System.Reflection;
using SAM.Core.Plugins;
namespace SAM.PluginHost;
public sealed class PluginLoader {
    private PluginRuntime? _runtime;

    public PluginUnloadReport Unload() => _runtime?.Stop() ?? new([], []);

    public IReadOnlyList<ISamPlugin> LoadFrom(string directory) {
        return LoadWithReport(directory).Plugins;
    }

    public PluginLoadReport LoadWithReport(string directory) {
        if (!Directory.Exists(directory)) return new([], []);
        var registry = new PluginRegistry();
        var failures = new List<PluginLoadFailure>();
        foreach (var file in Directory.EnumerateFiles(directory,"*.dll")) {
            try {
                var asm = Assembly.LoadFrom(file);
                foreach (var t in asm.GetTypes()) {
                    if (t.IsAbstract || !typeof(ISamPlugin).IsAssignableFrom(t)) continue;
                    if (Activator.CreateInstance(t) is ISamPlugin p) { p.Initialize(); registry.Register(p); }
                }
            } catch (Exception exception) { failures.Add(new(file, exception.Message)); }
        }
        var plugins = registry.Plugins.ToArray();
        _runtime = new PluginRuntime(plugins);
        return new(plugins, failures);
    }
}
