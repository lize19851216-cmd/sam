using System.Reflection;
using SAM.Core.Plugins;
namespace SAM.PluginHost;
public sealed class PluginLoader(PluginIsolationPolicy? isolationPolicy = null) {
    private PluginRuntime? _runtime;
    private readonly PluginIsolationPolicy _isolationPolicy = isolationPolicy ?? new PluginIsolationPolicy();

    public PluginUnloadReport Unload() => _runtime?.Stop() ?? new([], []);

    public IReadOnlyList<ISamPlugin> LoadFrom(string directory) {
        return LoadWithReport(directory).Plugins;
    }

    public PluginLoadReport LoadWithReport(string directory) {
        if (!Directory.Exists(directory)) return new([], []);
        var registry = new PluginRegistry();
        var failures = new List<PluginLoadFailure>();
        var trustPolicy = PluginTrustPolicy.FromManifest(directory);
        foreach (var file in Directory.EnumerateFiles(directory,"*.dll").OrderBy(path => path, StringComparer.OrdinalIgnoreCase)) {
            try {
                var decision = _isolationPolicy.Decide(file, trustPolicy);
                if (!decision.CanExecute) {
                    failures.Add(new(file, decision.Message));
                    continue;
                }
                var asm = Assembly.LoadFrom(file);
                foreach (var t in asm.GetTypes()) {
                    if (t.IsAbstract || !typeof(ISamPlugin).IsAssignableFrom(t)) continue;
                    if (Activator.CreateInstance(t) is ISamPlugin p) { p.Initialize(); registry.Register(p); }
                }
            } catch (Exception exception) { failures.Add(new(file, DescribeFailure(exception))); }
        }
        var plugins = registry.Plugins.ToArray();
        _runtime = new PluginRuntime(plugins);
        return new(plugins, failures);
    }

    private static string DescribeFailure(Exception exception) => exception switch
    {
        BadImageFormatException => "Plugin assembly is invalid.",
        FileNotFoundException => "Plugin assembly or a dependency is unavailable.",
        ReflectionTypeLoadException => "Plugin types could not be inspected.",
        _ => "Plugin could not be loaded."
    };
}
