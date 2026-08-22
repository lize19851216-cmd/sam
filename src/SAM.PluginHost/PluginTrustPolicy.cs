using System.Security.Cryptography;

namespace SAM.PluginHost;

/// <summary>Default-deny policy for in-process plugins. Only assemblies listed by SHA-256 may execute.</summary>
public sealed class PluginTrustPolicy
{
    public const string ManifestFileName = "trusted-plugins.sha256";
    private readonly HashSet<string> _trustedHashes;

    public PluginTrustPolicy(IEnumerable<string> trustedHashes)
    {
        ArgumentNullException.ThrowIfNull(trustedHashes);
        _trustedHashes = trustedHashes
            .Select(NormalizeHash)
            .Where(hash => hash.Length == 64 && hash.All(Uri.IsHexDigit))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static PluginTrustPolicy FromManifest(string pluginDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginDirectory);
        var manifestPath = Path.Combine(pluginDirectory, ManifestFileName);
        return !File.Exists(manifestPath)
            ? new PluginTrustPolicy([])
            : new PluginTrustPolicy(File.ReadLines(manifestPath)
                .Select(line => line.Split('#', 2)[0].Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    public bool IsTrusted(string assemblyPath) => _trustedHashes.Contains(CalculateHash(assemblyPath));

    public static string CalculateHash(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string NormalizeHash(string hash) => hash.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
}
