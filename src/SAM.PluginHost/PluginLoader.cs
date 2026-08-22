using System.Reflection;
using SAM.Core.Plugins;
namespace SAM.PluginHost;
public sealed class PluginLoader {
    public IReadOnlyList<ISamPlugin> LoadFrom(string directory) {
        if (!Directory.Exists(directory)) return [];
        var result = new List<ISamPlugin>();
        foreach (var file in Directory.EnumerateFiles(directory,"*.dll")) {
            try {
                var asm = Assembly.LoadFrom(file);
                foreach (var t in asm.GetTypes()) {
                    if (t.IsAbstract || !typeof(ISamPlugin).IsAssignableFrom(t)) continue;
                    if (Activator.CreateInstance(t) is ISamPlugin p) { p.Initialize(); result.Add(p); }
                }
            } catch { }
        }
        return result;
    }
}
