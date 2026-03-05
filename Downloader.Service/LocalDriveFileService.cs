using System.Diagnostics;
using ClosedXML.Excel;
using Downloader.Abstraction.Interfaces.Model;
using Downloader.Abstraction.Interfaces.Services;
using Downloader.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using Downloader.Abstraction.Enum;
using Downloader.Extensions;

namespace Downloader.Service
{
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
    public class LocalDriveFileService : IFileService
    {
        private static readonly HashSet<string> WrapperExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".aspx", ".asp", ".php", ".ashx", ".jsp", ".do", ".html", ".htm", ".shtml", ".page",
        };

        private static readonly HashSet<string> CompressionExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".gz", ".gzip", ".bz2", ".xz", ".zst",
        };

        private readonly ILogger<LocalDriveFileService> logger;
        private readonly IInputReaderService inputReader;
        private readonly IOptions<DownloaderSettings> options;

        public LocalDriveFileService(
            ILogger<LocalDriveFileService> logger, IInputReaderService inputReader,
            IOptions<DownloaderSettings> options)
        {
            this.logger = logger;
            this.inputReader = inputReader;
            this.options = options;
        }

        /// <inheritdoc />
        public async Task<IList<IDownloadTarget>> LoadTargetsFromInput()
        {
            // string inputSourceFile = options.Value.FilesToDownloadExcelInput;
            //CHANGE: This is done for easier Integrationtesting
            string inputSourceFile = Environment.GetEnvironmentVariable("INPUT_FILE") ?? options.Value.FilesToDownloadExcelInput;

            logger.LogInformation("Loading download targets from input file: {InputFile}", inputSourceFile);

            var sw = Stopwatch.StartNew();
            var targets = await inputReader.LoadTargets(inputSourceFile);
            sw.Stop();

            logger.LogInformation("Loaded {TargetCount} download targets from input file in {ElapsedMs} ms.",
                targets.Count, sw.ElapsedMilliseconds);

            return targets;
        }

        /// <inheritdoc />
        public async Task ExportDownloadedFile(IDownloadTarget target, Stream fileStream)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            string fileName = target.OutputFileName;
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("File name must be provided.", nameof(fileName));

            if (fileStream is null)
                throw new ArgumentNullException(nameof(fileStream));

            string exportPath = options.Value.DownloadedFilesOutputPath;

            string? downloadSourceLink = GetUsedDownloadLink(target);

            logger.LogInformation("Exporting downloaded file '{FileName}' from source '{SourceLink}'.", fileName,
                downloadSourceLink);

            Directory.CreateDirectory(exportPath);
            logger.LogDebug("Ensured export directory exists: {ExportPath}", exportPath);

            string extension = GetExtensionFromLink(downloadSourceLink);
            logger.LogDebug("Resolved extension '{Extension}' from source link.", extension);

            string finalFileName = string.IsNullOrEmpty(extension) ? fileName : fileName + extension;
            string fullPath = Path.Combine(exportPath, finalFileName);
            logger.LogDebug("Resolved output file path: {FullPath}", fullPath);
            target.FullOutputFileName = fullPath;

            if (fileStream.CanSeek && fileStream.Position != 0)
            {
                logger.LogDebug("Input stream position was {Position}; resetting to 0 before writing.",
                    fileStream.Position);
                fileStream.Position = 0;
            }

            var sw = Stopwatch.StartNew();

            await using var file = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None,
                81920, true);

            await fileStream.CopyToAsync(file);
            await file.FlushAsync();

            target.OutputFileSize = file.Length;

            sw.Stop();

            logger.LogInformation("Exported file '{FullPath}' successfully in {ElapsedMs} ms.", fullPath,
                sw.ElapsedMilliseconds);
        }

        private string? GetUsedDownloadLink(IDownloadTarget target)
        {
            return target.DownloadedUsing switch
            {
                DownloadedUsing.PRIMARY => target.PrimaryLink,
                DownloadedUsing.SECONDARY => target.SecondaryLink,
                var _ => throw new InvalidOperationException(
                    $"{nameof(target)} has marked the file to be downloaded from {nameof(DownloadedUsing.NONE)} which is invalid."),
            };
        }

        private string GetExtensionFromLink(string? downloadSourceLink)
        {
            return !TryGetFilePart(downloadSourceLink, out string filePart)
                ? string.Empty
                : ResolveExtensionFromFilePart(filePart);
        }

        private bool TryGetFilePart(string? downloadSourceLink, out string filePart)
        {
            filePart = string.Empty;

            if (string.IsNullOrWhiteSpace(downloadSourceLink))
            {
                logger.LogDebug("No source link provided; cannot determine extension.");
                return false;
            }

            string normalized = NormalizeLink(downloadSourceLink);

            filePart = ExtractFilePart(normalized);

            if (string.IsNullOrEmpty(filePart))
            {
                logger.LogDebug("Could not extract file part from source link: {SourceLink}", downloadSourceLink);

                return false;
            }

            return true;
        }

        private string ResolveExtensionFromFilePart(string filePart)
        {
            string lastExt = Path.GetExtension(filePart);

            if (string.IsNullOrEmpty(lastExt))
            {
                logger.LogDebug("No extension found in file part '{FilePart}'.", filePart);
                return string.Empty;
            }

            if (IsWrapperExtension(lastExt))
                return ResolveWrapperExtension(filePart, lastExt);

            if (IsCompressionExtension(lastExt))
                return ResolveCompressionExtension(filePart, lastExt);

            logger.LogDebug("Using extension '{Extension}' from file part '{FilePart}'.", lastExt, filePart);
            return lastExt;
        }

        private string ResolveWrapperExtension(string filePart, string wrapperExt)
        {
            string? recovered = RecoverExtensionBeforeWrapper(filePart, wrapperExt);

            logger.LogDebug("Wrapper extension '{WrapperExt}' detected. Recovered '{RecoveredExt}' from '{FilePart}'.",
                wrapperExt, recovered ?? "(none)", filePart);

            return recovered ?? string.Empty;
        }

        private string ResolveCompressionExtension(string filePart, string compressionExt)
        {
            string combined = BuildDoubleExtensionIfPossible(filePart, compressionExt);

            logger.LogDebug(
                "Compression extension '{CompressionExt}' detected. Using '{CombinedExt}' from '{FilePart}'.",
                compressionExt, combined, filePart);

            return combined;
        }

        /// <summary>
        /// Normalizes a download source link to a clean path that can be used for file name
        /// and extension extraction.
        ///
        /// The method performs the following steps:
        /// <list type="number">
        /// <item><description>Trims surrounding whitespace.</description></item>
        /// <item><description>Decodes HTML entities (e.g. <c>&amp;amp;</c> → <c>&amp;</c>).</description></item>
        /// <item><description>Removes fragment identifiers (anything after <c>#</c>).</description></item>
        /// <item><description>Removes query strings (anything after <c>?</c>).</description></item>
        /// <item><description>If the link is an absolute URI, reduces it to its <c>AbsolutePath</c>.</description></item>
        /// </list>
        ///
        /// Examples:
        /// <example>
        /// <code>
        /// https://example.com/report.pdf?la=en            -> /report.pdf
        /// https://example.com/report.pdf#zoom=50          -> /report.pdf
        /// https://example.com/report.pdf?la=en&amp;utm=abc    -> /report.pdf
        /// https://example.com/docs/report.pdf             -> /docs/report.pdf
        /// /docs/report.pdf                                -> /docs/report.pdf
        /// report.pdf                                      -> report.pdf
        /// </code>
        /// </example>
        /// </summary>
        private string NormalizeLink(string link)
        {
            string trimmed = link.Trim();
            string decoded = WebUtility.HtmlDecode(trimmed);

            string withoutFragment = CutAtFirst(decoded, '#');
            string withoutQuery = CutAtFirst(withoutFragment, '?');
            string path = ToPath(withoutQuery);

            if (!string.Equals(link, path, StringComparison.Ordinal))
                logger.LogDebug("Normalized source link from '{Original}' to '{Normalized}'.", link, path);

            return path;
        }

        private string ToPath(string linkOrPath)
        {
            if (!Uri.TryCreate(linkOrPath, UriKind.Absolute, out Uri? uri))
                return linkOrPath;

            logger.LogDebug("Parsed absolute URI; using AbsolutePath '{AbsolutePath}'.", uri.AbsolutePath);
            return uri.AbsolutePath;
        }

        private string ExtractFilePart(string path)
        {
            string normalized = path.Replace('\\', '/');
            int lastSlash = normalized.LastIndexOf('/');
            return lastSlash >= 0 ? normalized[(lastSlash + 1)..] : normalized;
        }

        private string CutAtFirst(string input, char delimiter)
        {
            int idx = input.IndexOf(delimiter);
            return idx >= 0 ? input[..idx] : input;
        }

        private bool IsWrapperExtension(string extension)
        {
            return WrapperExtensions.Contains(extension);
        }

        private bool IsCompressionExtension(string extension)
        {
            return CompressionExtensions.Contains(extension);
        }

        private string BuildDoubleExtensionIfPossible(string filePart, string compressionExt)
        {
            string withoutCompression = Path.GetFileNameWithoutExtension(filePart);
            string previousExt = Path.GetExtension(withoutCompression);

            string result = string.IsNullOrEmpty(previousExt) ? compressionExt : previousExt + compressionExt;

            logger.LogDebug(
                "Double-extension resolution: filePart='{FilePart}', previousExt='{PreviousExt}', compressionExt='{CompressionExt}', result='{Result}'.",
                filePart, string.IsNullOrEmpty(previousExt) ? "(none)" : previousExt, compressionExt, result);

            return result;
        }

        /// <summary>
        /// Attempts to recover the real file extension when a URL ends with a wrapper extension
        /// such as <c>.aspx</c>, <c>.php</c>, etc. Examples:
        /// <example>
        /// <code>
        /// Report_2017.pdf.aspx  ->  .pdf
        /// download.xml.ashx     ->  .xml
        /// </code>
        /// </example>
        /// </summary>
        private string? RecoverExtensionBeforeWrapper(string filePart, string wrapperExt)
        {
            string withoutWrapper = filePart[..^wrapperExt.Length];
            string previousExt = Path.GetExtension(withoutWrapper);
            return string.IsNullOrEmpty(previousExt) ? null : previousExt;
        }

        /// <inheritdoc />
        public async Task ExportReport(string content, string fileExtension)
        {
            if (string.IsNullOrWhiteSpace(content))
                throw new ArgumentException("Content cannot be null or empty.", nameof(content));

            if (string.IsNullOrWhiteSpace(fileExtension))
                throw new ArgumentException("File extension cannot be null or empty.", nameof(fileExtension));

            // string exportPath = options.Value.ReportsOutputPath;
            //CHANGE: This is done for easier Integrationtesting
            string exportPath = Environment.GetEnvironmentVariable("WORK_DIR") ?? options.Value.ReportsOutputPath;

            logger.LogInformation("Starting report export. Extension: {Extension}", fileExtension);

            string fullPath = BuildReportOutputPath(exportPath, fileExtension);

            await WriteReportFile(fullPath, content);
        }

        private string BuildReportOutputPath(string exportPath, string fileExtension)
        {
            if (string.IsNullOrWhiteSpace(exportPath))
                throw new InvalidOperationException("ReportsOutputPath is not configured.");

            Directory.CreateDirectory(exportPath);

            fileExtension = fileExtension.Trim().TrimStart('.');

            var datePart = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            // var fileName = $"report-{datePart}.{fileExtension}";
            //CHANGE: This is done for easier Integrationtesting
            var fileName = Environment.GetEnvironmentVariable("REPORT_FILE") ?? $"report-{datePart}.{fileExtension}";
            string fullPath = Path.Combine(exportPath, fileName);

            logger.LogDebug("Resolved report file path. FileName: {FileName}, FullPath: {FullPath}", fileName,
                fullPath);

            return fullPath;
        }

        private async Task WriteReportFile(string fullPath, string content)
        {
            try
            {
                //FEEDBACK: Does this have to be async? Its one file, one report
                await File.WriteAllTextAsync(fullPath, content);

                logger.LogInformation("Report successfully exported to {FullPath}. Size: {ContentLength} characters",
                    fullPath, content.Length);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to export report to {FullPath}", fullPath);
                throw;
            }
        }
    }
}