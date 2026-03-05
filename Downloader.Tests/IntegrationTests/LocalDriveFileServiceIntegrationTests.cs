using NUnit.Framework;
using NUnit.Framework.Internal;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Downloader.Tests.IntegrationTests;

[TestFixture]
public class LocalDriveFileServiceIntegrationTests
{
    private string _exePath;

    [OneTimeSetUp]
    public void Setup()
    {
        // Path to the built console app
         _exePath = Path.GetFullPath(@"..\..\..\..\Downloader.Executor\bin\Debug\net10.0\Downloader.Executor.exe");
    }

    [Test]
    public async Task RunCommand_ProcessesRealExcelFile_AndCreatesReport()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        var excelPath = Path.Combine(tempDir.FullName, "input.xlsx");
        File.Copy("../../../IntegrationTests/TestFiles/TestData1.xlsx", excelPath);

        using var process = StartApp(tempDir.FullName, excelPath);

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        await process.StandardInput.WriteLineAsync("run");
        process.StandardInput.Close();

        await Task.WhenAll(outputTask, errorTask);
        await process.WaitForExitAsync();

        var output = await outputTask;
        var error = await errorTask;

        if (Debugger.IsAttached) {
            Process.Start("explorer.exe", tempDir.FullName); 
        }

        Assert.That(process.ExitCode, Is.EqualTo(0),
            $"Exit code: {process.ExitCode}\nOutput:\n{output}\nError:\n{error}");

        var reportPath = Path.Combine(tempDir.FullName, "report.md");
        Assert.That(File.Exists(reportPath), "Report file missing");

        var report = File.ReadAllText(reportPath);
        Assert.That(report.Length, Is.GreaterThan(10), "Report appears empty or too small");
        Assert.That(report, Does.Contain("#"), "Report missing Markdown headings");
        Assert.That(process.ExitCode, Is.EqualTo(0));

    }


    private Process StartApp(string workingDir, string inputFile)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _exePath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDir,
            UseShellExecute = false
        };

        psi.EnvironmentVariables["INPUT_FILE"] = inputFile;
        psi.EnvironmentVariables["WORK_DIR"] = workingDir;
        psi.EnvironmentVariables["REPORT_FILE"] = "report.md";

        return Process.Start(psi)!;
    }

}
