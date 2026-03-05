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
    public class ExeStartup : ModularStartup
    {
        private const string SETTINGS_SECTIONS = "Downloader";
        private const string SHARED_ROOT_FOLDER_NAME = "FangSoftware";
        private const string APP_FOLDER_NAME = "FileDownloader";
        private readonly DownloaderSettings defaultDownloaderSettings;

        public ExeStartup()
        {
            AddModule(new LoggingStartupModule(GetApplicationDataPath()));

            defaultDownloaderSettings = new DownloaderSettings
            {
                DownloadedFilesOutputPath = Path.Combine(GetApplicationDataPath(), "Downloads"),
                ReportsOutputPath = Path.Combine(GetApplicationDataPath(), "Reports"),
                FilesToDownloadExcelInput = Path.Combine(GetApplicationDataPath(), "GRI_2017_2020.xlsx"),
            };
        }

        public string GetApplicationDataPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                SHARED_ROOT_FOLDER_NAME, APP_FOLDER_NAME);
        }

        public IHost BuildHost(string[] args)
        {
            return Host.CreateDefaultBuilder(args).ConfigureAppConfiguration(ConfigureAppConfiguration)
                .ConfigureServices(SetupServices).Build();
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
                .Validate(DownloaderSettings.IsValid).ValidateOnStart();

            services.AddScoped<IOrchestratorService, OrchestratorService>();
            services.AddScoped<IDownloadService, DownloadService>();
            services.AddScoped<IFileService, LocalDriveFileService>();
            services.AddScoped<IReportService, MarkdownReportService>();
            services.AddScoped<IInputReaderService, ExcelInputReaderService>();
            services.AddHttpClient<IHttpFileDownloaderService, HttpFileDownloaderService>();
        }
    }
}