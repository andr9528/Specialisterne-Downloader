using Downloader.Abstraction.Interfaces.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace Downloader.Executor.Startup.Modules
{
    public class LoggingStartupModule : IStartupModule
    {
        private const string LOG_PATTERN =
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u}] [{SourceContext}] {Scope} {Message:lj}{NewLine}{Exception}";

        public static string LogDirectory = null!;

        public LoggingStartupModule(string applicationDataPath)
        {
            // LogDirectory = Path.Combine(applicationDataPath, "Logs");
            //CHANGE: This is done for easier Integrationtesting
            applicationDataPath = Environment.GetEnvironmentVariable("WORK_DIR") ?? applicationDataPath;
            LogDirectory = Path.Combine(applicationDataPath, "Logs");
        }

        /// <inheritdoc />
        public void ConfigureServices(HostBuilderContext hostBuilderContext, IServiceCollection services)
        {
            var level = LogEventLevel.Information;
#if DEBUG
            level = LogEventLevel.Debug;
#endif

            LoggerConfiguration configuration = new LoggerConfiguration().Enrich.FromLogContext();
            configuration = AddWriteToSegments(configuration);
            configuration = ConfigureMinimumLevel(configuration);
            configuration = AddLevelOverwrites(configuration);

            Log.Logger = configuration.CreateLogger();

            services.AddLogging(x =>
            {
                x.ClearProviders();
                x.AddSerilog(Log.Logger);
            });

            var todayFileName = $"log-{DateTime.Now:yyyyMMdd}.log";
            var fullPath = Path.Combine(LogDirectory, todayFileName);

            if (File.Exists(fullPath))
            {
                File.AppendAllText(fullPath, Environment.NewLine);
                File.AppendAllText(fullPath, Environment.NewLine);
                File.AppendAllText(fullPath, Environment.NewLine);
            }

            var logger = services.BuildServiceProvider().GetService<ILogger<LoggingStartupModule>>();
            logger?.LogDebug("Completed Configuration of Logging Services.");
        }

        private LoggerConfiguration ConfigureMinimumLevel(LoggerConfiguration configuration)
        {
            configuration = configuration.MinimumLevel.Debug();

            return configuration;
        }

        private LoggerConfiguration AddWriteToSegments(LoggerConfiguration configuration)
        {
            var level = LogEventLevel.Information;
#if DEBUG
            level = LogEventLevel.Debug;
#endif

            string logPath = Path.Combine(LogDirectory, "log-.log");

            configuration = configuration.WriteTo.Console(outputTemplate: LOG_PATTERN);
            configuration = configuration.WriteTo.File(logPath, outputTemplate: LOG_PATTERN, shared: true,
                flushToDiskInterval: TimeSpan.FromMinutes(1), restrictedToMinimumLevel: level,
                retainedFileCountLimit: 7, rollingInterval: RollingInterval.Day);

            return configuration;
        }

        private LoggerConfiguration AddLevelOverwrites(LoggerConfiguration configuration)
        {
            configuration = configuration.MinimumLevel.Override("Microsoft", LogEventLevel.Warning);
            configuration =
                configuration.MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Information);

            configuration = configuration.MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning);
            configuration = configuration.MinimumLevel.Override("Microsoft.Extensions.Http", LogEventLevel.Warning);

            return configuration;
        }
    }
}