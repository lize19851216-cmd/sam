using System.IO.Pipes;

namespace SAM.PluginHost;

/// <summary>Validates and secures local endpoints used by the metadata-only plugin isolation transport.</summary>
public static class PluginIsolationEndpoint
{
    public const int MaximumPipeNameLength = 128;

    public static PipeOptions LocalUserPipeOptions => PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;

    public static string ValidatePipeName(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        if (pipeName.Length > MaximumPipeNameLength)
            throw new ArgumentOutOfRangeException(nameof(pipeName), $"Pipe names cannot exceed {MaximumPipeNameLength} characters.");
        if (pipeName.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            throw new ArgumentException("Pipe names may contain only ASCII letters, digits, hyphens, and underscores.", nameof(pipeName));

        return pipeName;
    }
}
