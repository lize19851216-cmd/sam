using Serilog;
namespace SAM.Infrastructure.Logging;
public static class SamLog {
    public static ILogger Create(string dir) {
        Directory.CreateDirectory(dir);
        return new LoggerConfiguration()
          .MinimumLevel.Information()
          .Enrich.FromLogContext()
          .Enrich.WithProperty("Application", "SAM")
          .WriteTo.File(Path.Combine(dir,"sam-.log"), rollingInterval: RollingInterval.Day,
              retainedFileCountLimit: 14,
              outputTemplate: "{Timestamp:O} [{Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{Exception}")
          .CreateLogger();
    }
}
