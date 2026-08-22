namespace SAM.Core.Plugins;
public interface ISamPlugin {
    string Id { get; }
    string Name { get; }
    Version Version { get; }
    void Initialize();
}
