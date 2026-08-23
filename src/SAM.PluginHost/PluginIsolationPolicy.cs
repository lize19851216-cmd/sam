namespace SAM.PluginHost;

/// <summary>Determines whether a plugin may execute in the SAM desktop process.</summary>
public sealed class PluginIsolationPolicy
{
    public PluginExecutionDecision Decide(string assemblyPath, PluginTrustPolicy trustPolicy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentNullException.ThrowIfNull(trustPolicy);

        if (trustPolicy.IsTrusted(assemblyPath))
        {
            return new PluginExecutionDecision(
                PluginExecutionMode.TrustedInProcess,
                "Assembly hash is trusted for in-process execution.");
        }

        return new PluginExecutionDecision(
            PluginExecutionMode.Rejected,
            $"Assembly is not trusted. Untrusted plugins cannot execute in-process; {PluginTrustPolicy.ManifestFileName} review is required.");
    }
}

/// <summary>Execution modes currently permitted by the plugin host.</summary>
public enum PluginExecutionMode
{
    Rejected,
    TrustedInProcess
}

public sealed record PluginExecutionDecision(PluginExecutionMode Mode, string Message)
{
    public bool CanExecute => Mode == PluginExecutionMode.TrustedInProcess;
}
