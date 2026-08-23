using Serilog;
namespace SAM.Infrastructure.Logging;
public static class SamLog {
    public const long DefaultFileSizeLimitBytes = 10 * 1024 * 1024;

    public static ILogger Create(string dir, long fileSizeLimitBytes = DefaultFileSizeLimitBytes) {
        if (fileSizeLimitBytes <= 0) throw new ArgumentOutOfRangeException(nameof(fileSizeLimitBytes));
        Directory.CreateDirectory(dir);
        return new LoggerConfiguration()
          .MinimumLevel.Information()
          .Enrich.FromLogContext()
          .Enrich.WithProperty("Application", "SAM")
          .WriteTo.File(Path.Combine(dir,"sam-.log"), rollingInterval: RollingInterval.Day,
              retainedFileCountLimit: 14,
              fileSizeLimitBytes: fileSizeLimitBytes,
              rollOnFileSizeLimit: true,
              outputTemplate: "{Timestamp:O} [{Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{Exception}")
          .CreateLogger();
    }
}
