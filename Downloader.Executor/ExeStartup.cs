using System.Text.Json;
using System.Text.Json.Nodes;
using Downloader.Abstraction.Interfaces.Services;
using Downloader.Executor.Startup;
using Downloader.Executor.Startup.Modules;
using Downloader.Model;
using Downloader.Service;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;

// Reduces readability in multiple location in this file, if done.
// ReSharper disable ConvertIfStatementToReturnStatement

namespace Downloader.Executor
{
    /// FEEDBACK
    /// S: Validate Methods could be DownloaderSettings own responsiblity. Since its job is to represent its own correct data(?)
    /// S: I am not certain it should take responsibility for Normalizing either. Also unsure if the application should even do this and not just report error to user, or create a funtionality to set settings in the application
    /// O:
    /// L:
    /// I:
    /// D:
    /// Naming: Generally good naming, maybe something on the bool return methods with IsNormalized or CanValidate
    /// Readability:
    /// Organisation: Good small methods
    /// Comments:
    /// Error Handling: Does not seem to be much error handling. Might be because we expect to default if settings is not configured correctly
    /// Logging: No real logging, but is setup. So is mostly for debugging
    /// Test Ideas:
    /// Other:
    public class ExeStartup : ModularStartup
    {
        private const string SETTINGS_SECTIONS = "Downloader";
        private const string SHARED_ROOT_FOLDER_NAME = "FangSoftware";
        private const string APP_FOLDER_NAME = "FileDownloader";
        private readonly DownloaderSettings defaultDownloaderSettings;

        public ExeStartup()
        {
            var environmentWorkingDirectory = Environment.GetEnvironmentVariable("WORK_DIR") ?? GetApplicationDataPath();

            AddModule(new LoggingStartupModule(environmentWorkingDirectory));

            defaultDownloaderSettings = new DownloaderSettings
            {
                DownloadedFilesOutputPath = Path.Combine(environmentWorkingDirectory, "Downloads"),
                ReportsOutputPath = Path.Combine(environmentWorkingDirectory, "Reports"),
                FilesToDownloadExcelInput = Path.Combine(environmentWorkingDirectory, "GRI_2017_2020.xlsx"),
            };
        }

        public string GetApplicationDataPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                SHARED_ROOT_FOLDER_NAME, APP_FOLDER_NAME);
        }

        public IHost BuildHost(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration(ConfigureAppConfiguration)
                .ConfigureServices(SetupServices)
                .Build();
        }

        private void ConfigureAppConfiguration(
            HostBuilderContext hostBuilderContext, IConfigurationBuilder configurationBuilder)
        {
            string appSettingsPath = Path.Combine(GetApplicationDataPath(), "appsettings.json");

            EnsureAndNormalizeAppSettings(appSettingsPath);

            configurationBuilder.AddJsonFile(appSettingsPath, false, true);
        }

        private void EnsureAndNormalizeAppSettings(string appSettingsPath)
        {
            string directory = Path.GetDirectoryName(appSettingsPath) ??
                               throw new InvalidOperationException(
                                   $"Could not determine directory for '{appSettingsPath}'.");

            Directory.CreateDirectory(directory);

            JsonObject rootObj = LoadOrCreateRootObject(appSettingsPath, out bool createdNewFile);

            bool changed = createdNewFile;

            JsonObject downloaderObj = EnsureDownloaderSection(rootObj, ref changed);
            changed |= NormalizeDownloaderBounds(downloaderObj);

            if (changed)
                WriteAppSettings(appSettingsPath, rootObj);
        }

        private JsonObject LoadOrCreateRootObject(string appSettingsPath, out bool createdNewFile)
        {
            createdNewFile = false;

            if (!File.Exists(appSettingsPath))
            {
                createdNewFile = true;
                return CreateDefaultRootObject();
            }

            try
            {
                string jsonText = File.ReadAllText(appSettingsPath);
                JsonNode? node = JsonNode.Parse(jsonText);

                if (node is JsonObject obj)
                    return obj;

                // If root isn't an object, treat it as corrupt and recreate
                createdNewFile = true;
                return CreateDefaultRootObject();
            }
            catch
            {
                // If parsing fails, fall back to default (fail-safe)
                createdNewFile = true;
                return CreateDefaultRootObject();
            }
        }

        private JsonObject CreateDefaultRootObject()
        {
            return new JsonObject
            {
                ["Downloader"] = JsonSerializer.SerializeToNode(defaultDownloaderSettings),
            };
        }

        private JsonObject EnsureDownloaderSection(JsonObject rootObj, ref bool changed)
        {
            if (rootObj["Downloader"] is JsonObject downloaderObj)
                return downloaderObj;

            downloaderObj = JsonSerializer.SerializeToNode(defaultDownloaderSettings) as JsonObject ?? new JsonObject();

            rootObj["Downloader"] = downloaderObj;
            changed = true;

            return downloaderObj;
        }

        private bool NormalizeDownloaderBounds(JsonObject downloaderObj)
        {
            var changed = false;

            changed |= NormalizeIntMinMinusOne(downloaderObj, nameof(DownloaderSettings.TargetStartIndex));

            changed |= NormalizeIntMinMinusOne(downloaderObj, nameof(DownloaderSettings.TargetEndIndex));

            return changed;
        }

        private bool NormalizeIntMinMinusOne(JsonObject section, string propertyName)
        {
            if (section[propertyName] is null)
                return false;

            if (section[propertyName] is not JsonValue val)
                return false;

            if (!val.TryGetValue<int>(out int current))
                return false;

            if (current >= -1)
                return false;

            section[propertyName] = -1;
            return true;
        }

        private void WriteAppSettings(string appSettingsPath, JsonObject rootObj)
        {
            string json = rootObj.ToJsonString(new JsonSerializerOptions {WriteIndented = true,});

            string tmpPath = appSettingsPath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Copy(tmpPath, appSettingsPath, true);
            File.Delete(tmpPath);
        }


        /// <inheritdoc />
        public override void ConfigureServices(HostBuilderContext hostBuilderContext, IServiceCollection services)
        {
            base.ConfigureServices(hostBuilderContext, services);

            services.AddOptions<DownloaderSettings>()
                .Bind(hostBuilderContext.Configuration.GetSection(SETTINGS_SECTIONS))
                .Validate(ValidateDownloaderSettings)
                .ValidateOnStart();

            services.AddScoped<IOrchestratorService, OrchestratorService>();
            services.AddScoped<IDownloadService, DownloadService>();
            services.AddScoped<IFileService, LocalDriveFileService>();
            services.AddScoped<IReportService, MarkdownReportService>();
            services.AddScoped<IInputReaderService, ExcelInputReaderService>();
            services.AddHttpClient<IHttpFileDownloaderService, HttpFileDownloaderService>();
        }

        private bool ValidateDownloaderSettings(DownloaderSettings? settings)
        {
            if (settings is null)
                return false;

            if (!ValidateStringSettings(settings))
                return false;

            if (!ValidateIntegerSettings(settings))
                return false;

            return true;
        }

        private bool ValidateIntegerSettings(DownloaderSettings settings)
        {
            // Normalize target slicing bounds (soft validation)
            if (settings.TargetStartIndex < -1)
                settings.TargetStartIndex = -1;

            if (settings.TargetEndIndex < -1)
                settings.TargetEndIndex = -1;

            // Must be at least 1 — otherwise nothing downloads
            if (settings.MaxConcurrentDownloads < 1)
                return false;

            // Retries must not be negative
            if (settings.DownloadRetries < 0)
                return false;

            // Waiting time must not be negative
            if (settings.SecondsWaitBetweenRetry < 0)
                return false;

            return true;
        }

        private bool ValidateStringSettings(DownloaderSettings settings)
        {
            // ---- Required string properties ----
            if (string.IsNullOrWhiteSpace(settings.DownloadedFilesOutputPath))
                return false;

            if (string.IsNullOrWhiteSpace(settings.ReportsOutputPath))
                return false;

            if (string.IsNullOrWhiteSpace(settings.FilesToDownloadExcelInput))
                return false;

            // ---- Ensure paths are absolute ----
            if (!Path.IsPathRooted(settings.DownloadedFilesOutputPath))
                return false;

            if (!Path.IsPathRooted(settings.ReportsOutputPath))
                return false;

            if (!Path.IsPathRooted(settings.FilesToDownloadExcelInput))
                return false;
            return true;
        }
    }
}