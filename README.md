# File Downloader (Downloader.Executor)

***Many parts of the code has been generated with use of ChatGPT***

Console app for downloading files listed in an Excel sheet, with configurable output folders, retries, and concurrency.

> Note: For now the program is run **from source**. A single-file release build is planned later.

---

## Prerequisites

- **.NET SDK** matching the repository’s target framework (check the `.csproj` / `global.json` in the repo).
- **Windows** - the interactive menu opens folders using `explorer.exe`.
- An **Excel file** containing download targets - see format below.

---

## Run from source

From the repository root:

```bash
dotnet restore
dotnet run --project Downloader.Executor
```

When the program starts, you’ll get an interactive menu (see commands below).

---

## Interactive Menu Commands

When the application starts, it enters an interactive command loop.

Available commands:

| Command     | Description                                                                   |
|-------------|-------------------------------------------------------------------------------|
| `reports`   | Opens the folder configured by `DownloaderSettings.ReportsOutputPath`         |
| `logs`      | Opens the log directory                                                       |
| `downloads` | Opens the folder configured by `DownloaderSettings.DownloadedFilesOutputPath` |
| `settings`  | Opens the application data folder                                             |
| `input`     | Opens the folder containing the Excel input file                              |
| `run`       | Executes the download workflow                                                |

All folder commands will:

1. Ensure the folder exists (create if necessary)
2. Open it in Windows Explorer

---

### Command Line Usage

Commands can also be executed directly from the command line without entering the interactive menu.

Use the argument format:

```bash
command=<command>
```

The `dotnet run` command must be executed from the [`Downloader.Executor`](Downloader.Executor) project folder, or target the folder with `--project Downloader.Executor`, like described earlier.

Example:

```bash
dotnet run -- command=run
```

When executed this way, the application will immediately run the specified command and then exit.

Examples:

| Example                           | Result                            |
|-----------------------------------|-----------------------------------|
| `dotnet run -- command=run`       | Executes the download workflow    |
| `dotnet run -- command=reports`   | Opens the reports folder          |
| `dotnet run -- command=logs`      | Opens the log directory           |
| `dotnet run -- command=downloads` | Opens the downloads folder        |
| `dotnet run -- command=settings`  | Opens the application data folder |

Note: The `--` is required when using `dotnet run`. Everything after it is passed to the application's `Main(string[] args)` method.

---

## NuGet Dependencies

### Application & Infrastructure

- Microsoft.Extensions.* (DI, Hosting, Logging, Configuration, Http)
- Serilog (Console & File logging)
- ClosedXML (Excel parsing)
- FluentMarkdown (Markdown report generation)

### Testing

- NUnit (Test framework)
- Moq (Mocking)
- FluentAssertions (Expressive assertions)
- Coverlet (Code coverage)
- GitHubActionsTestLogger (CI integration)

---

## Configuration

Configuration is bound to the `DownloaderSettings` class using `IOptions<DownloaderSettings>`.

Settings are defined in `appsettings.json` under the `Downloader` section.

### Example `appsettings.json`

```json
{
  "Downloader": {
    "ReportsOutputPath": "C:\\Temp\\FileDownloader\\Reports",
    "DownloadedFilesOutputPath": "C:\\Temp\\FileDownloader\\Downloads",
    "FilesToDownloadExcelInput": "C:\\Temp\\FileDownloader\\Input\\files.xlsx",

    "MaxConcurrentDownloads": 5,
    "DownloadRetries": 3,
    "SecondsWaitBetweenRetry": 5,

    "TargetStartIndex": -1,
    "TargetEndIndex": -1
  }
}

```

## Settings Reference

### Paths

#### `ReportsOutputPath` *(string, required)*

Full path to the directory where generated reports are stored.

Example:

```text
C:\Temp\FileDownloader\Reports
```

#### `DownloadedFilesOutputPath` *(string, required)*

Full path to the directory where downloaded files are saved.

Example:

```text
C:\Temp\FileDownloader\Downloads
```

#### `FilesToDownloadExcelInput` *(string, required)*

Full path to the Excel file containing download targets.

Expected column structure:

| Column | Description              |
|--------|--------------------------|
| A      | Output file name         |
| AL     | Primary download link    |
| AM     | Secondary download link  |

Absolute paths are highly recommended.

### Download Behaviour

#### `MaxConcurrentDownloads` *(int, default: 5)*

Maximum number of concurrent downloads.

Recommended range: `3–10`

#### `DownloadRetries` *(int, default: 3)*

Number of retry attempts before marking a link as failed.

Recommended range: `1–5`

#### `SecondsWaitBetweenRetry` *(int, default: 5)*

Delay in seconds between retry attempts.

Recommended range: `1–15`

### Target Range Filtering

#### `TargetStartIndex` *(int, default: -1)*

Lower inclusive bound of targets to process.

- `-1` → No lower bound  
- `0` → Start from first target  
- `10` → Start from 11th target  

#### `TargetEndIndex` *(int, default: -1)*

Upper inclusive bound of targets to process.

- `-1` → No upper bound  
- `99` → Stop at 100th target  

---

## Notes

- The application is currently Windows only due to use of `explorer.exe`.

---

## Future

- Single-file self-contained release build.
- Move targeted columns for input data to settings, to support changing the input columns.
- Setup skipping retries, depending on the response gotten.
  - E.g 403, i.e Forbidden, wont change with a retry.
- Configure `HttpClient` used more precisely.
  - E.g to bypass sites rejecting the request due to suspected crawler / AI activty
- Add configurable maximum time to download file.
