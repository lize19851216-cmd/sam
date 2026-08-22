namespace SAM.Core.Plugins;
public interface ISamPlugin {
    string Id { get; }
    string Name { get; }
    Version Version { get; }
    void Initialize();

    /// <summary>Releases host-owned plugin resources before the plugin is unloaded.</summary>
    void Shutdown() { }
}
