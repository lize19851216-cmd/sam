using Serilog;
namespace SAM.Infrastructure.Logging;
public static class SamLog {
    public static ILogger Create(string dir) {
        Directory.CreateDirectory(dir);
        return new LoggerConfiguration()
          .MinimumLevel.Information()
          .WriteTo.File(Path.Combine(dir,"sam-.log"),rollingInterval:RollingInterval.Day,retainedFileCountLimit:14)
          .CreateLogger();
    }
}
