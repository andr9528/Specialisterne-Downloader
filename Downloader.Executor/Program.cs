using Downloader.Abstraction.Interfaces.Services;
using Downloader.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using Downloader.Executor.Startup.Modules;

// ReSharper disable InvertIf
// ReSharper disable MemberCanBeMadeStatic.Local

namespace Downloader.Executor
{
    internal class Program : IAsyncDisposable
    {
        private readonly ExeStartup startup;
        private readonly IHost host;
        private readonly IServiceScope scope;

        private readonly DownloaderSettings options;
        private readonly IOrchestratorService orchestrator;

        private Program(string[] args)
        {
            startup = new ExeStartup();
            host = startup.BuildHost(args);
            scope = host.Services.CreateScope();

            options = scope.ServiceProvider.GetRequiredService<IOptions<DownloaderSettings>>().Value;
            orchestrator = scope.ServiceProvider.GetRequiredService<IOrchestratorService>();
        }

        /// FEEDBACK
        /// S:
        /// O:
        /// L:
        /// I:
        /// D:
        /// Naming:
        /// Readability:
        /// Organisation:
        /// Comments:
        /// Error Handling:
        /// Logging:
        /// Test Ideas:
        /// Other:
        private static async Task Main(string[] args)
        {
            var program = new Program(args);

            await program.RunInteractiveMenu();
        }

        public async ValueTask DisposeAsync()
        {
            scope.Dispose();
            host.Dispose();
            await Task.CompletedTask;
        }

        private async Task RunInteractiveMenu()
        {
            while (true)
            {
                PrintMenu();

                Console.Write("> ");
                string? input = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(input))
                {
                    Console.WriteLine("Invalid input. Try again.\n");
                    continue;
                }

                bool handled = await HandleCommand(input.Trim());

                if (!handled)
                {
                    Console.WriteLine("Unknown command. Try again.\n");
                    continue;
                }

                if (input.Equals("run", StringComparison.InvariantCultureIgnoreCase))
                    return; // exit after run
            }
        }

        private async Task<bool> HandleCommand(string command)
        {
            if (command.Equals("reports", StringComparison.InvariantCultureIgnoreCase))
            {
                OpenFolder(options.ReportsOutputPath);
                return true;
            }

            if (command.Equals("logs", StringComparison.InvariantCultureIgnoreCase))
            {
                OpenFolder(LoggingStartupModule.LogDirectory);
                return true;
            }

            if (command.Equals("downloads", StringComparison.InvariantCultureIgnoreCase))
            {
                OpenFolder(options.DownloadedFilesOutputPath);
                return true;
            }

            if (command.Equals("settings", StringComparison.InvariantCultureIgnoreCase))
            {
                OpenFolder(startup.GetApplicationDataPath());
                return true;
            }

            if (command.Equals("input", StringComparison.InvariantCultureIgnoreCase))
            {
                string folder = GetFolderFromFilePath(options.FilesToDownloadExcelInput);
                OpenFolder(folder);
                return true;
            }

            if (command.Equals("run", StringComparison.InvariantCultureIgnoreCase))
            {
                await orchestrator.InitiateWorkflow();
                return true;
            }

            return false;
        }

        private void OpenFolder(string? folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                return;

            Directory.CreateDirectory(folderPath);

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folderPath}\"",
                UseShellExecute = true,
            });
        }

        private string GetFolderFromFilePath(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return Environment.CurrentDirectory;

            string fullPath = Path.GetFullPath(filePath);
            return Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;
        }

        private void PrintMenu()
        {
            Console.WriteLine("Available commands:");
            Console.WriteLine("  reports   - Open reports output folder");
            Console.WriteLine("  logs      - Open logs folder");
            Console.WriteLine("  downloads - Open downloads output folder");
            Console.WriteLine("  settings  - Open application data folder");
            Console.WriteLine("  input     - Open folder containing Excel input file");
            Console.WriteLine("  run       - Run the workflow");
            Console.WriteLine();
        }
    }
}