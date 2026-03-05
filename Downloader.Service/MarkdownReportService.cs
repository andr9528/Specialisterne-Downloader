using DocumentFormat.OpenXml.Office2019.Drawing;
using Downloader.Abstraction.Enum;
using Downloader.Abstraction.Interfaces.Model;
using Downloader.Abstraction.Interfaces.Services;
using Downloader.Extensions;
using Downloader.Model;
using FluentMarkdown;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace Downloader.Service
{
    public class MarkdownReportService : IReportService
    {
        private readonly ILogger<MarkdownReportService> logger;
        private readonly IOptions<DownloaderSettings> options;

        public MarkdownReportService(ILogger<MarkdownReportService> logger, IOptions<DownloaderSettings> options)
        {
            this.logger = logger;
            this.options = options;
        }

        /// <inheritdoc />
        public string GenerateReport(IList<IDownloadTarget> targets, TimeSpan? timeSpentDownloading)
        {
            if (targets is null)
                throw new ArgumentNullException(nameof(targets));

            string downloadExportPath = options.Value.DownloadedFilesOutputPath;

            DateTime createdAt = DateTime.Now;
            var createdAtText = createdAt.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture);

            logger.LogInformation(
                "Generating markdown report. Targets: {TargetCount}, DownloadExportPath: {DownloadExportPath}",
                targets.Count, downloadExportPath);

            var (primary, secondary, none) = SplitTargets(targets);

            logger.LogDebug(
                "Report target groups. Primary: {PrimaryCount}, Secondary: {SecondaryCount}, None: {NoneCount}",
                primary.Count, secondary.Count, none.Count);

            var builder = new MarkdownBuilder();

            AddHeaderAndMetadata(builder, createdAtText, downloadExportPath, timeSpentDownloading);
            AddCountMetadata(builder, primary.Count, secondary.Count, none.Count);
            AddPrimarySection(builder, primary);
            AddSecondarySection(builder, secondary);
            AddNoneSection(builder, none);

            return builder.ToString();
        }

        private void AddCountMetadata(MarkdownBuilder builder, int primaryCount, int secondaryCount, int noneCount)
        {
            builder.AddParagraph($"Processed a total of {primaryCount + secondaryCount + noneCount}.")
                .AddParagraph($"Of those, a total of {primaryCount + secondaryCount} files were downloaded.")
                .AddParagraph($"Finally, {noneCount} failed to download for one reason or another.");
        }

        private (List<IDownloadTarget> Primary, List<IDownloadTarget> Secondary, List<IDownloadTarget> None)
            SplitTargets(IList<IDownloadTarget> targets)
        {
            var primary = new List<IDownloadTarget>();
            var secondary = new List<IDownloadTarget>();
            var none = new List<IDownloadTarget>();

            foreach (IDownloadTarget t in targets)
            {
                switch (t.DownloadedUsing)
                {
                    case DownloadedUsing.PRIMARY:
                        primary.Add(t);
                        break;
                    case DownloadedUsing.SECONDARY:
                        secondary.Add(t);
                        break;
                    default:
                        none.Add(t);
                        break;
                }
            }

            return (primary, secondary, none);
        }

        private void AddHeaderAndMetadata(
            MarkdownBuilder builder, string createdAtText, string downloadExportPath, TimeSpan? timeSpentDownloading)
        {
            var timeSpendParagraph = timeSpentDownloading.HasValue
                ? $"In total it took {FormatDurationHumanReadable(timeSpentDownloading.Value)} to complete the download."
                : "No time was spent downloading.";

            builder.AddHeader(1, "Download Report").AddParagraph($"Created: {createdAtText}")
                .AddParagraph($"Downloaded files output path: `{downloadExportPath}`").AddParagraph(timeSpendParagraph);
        }

        private string FormatDurationHumanReadable(TimeSpan time)
        {
            var parts = new List<string>();

            if (time.Hours > 0)
                parts.Add($"{time.Hours} {(time.Hours == 1 ? "hour" : "hours")}");

            if (time.Minutes > 0)
                parts.Add($"{time.Minutes} {(time.Minutes == 1 ? "minute" : "minutes")}");

            // Always include seconds (even if 0)
            parts.Add($"{time.Seconds} {(time.Seconds == 1 ? "second" : "seconds")}");

            return string.Join(" ", parts);
        }

        private void AddPrimarySection(MarkdownBuilder builder, IList<IDownloadTarget> primary)
        {
            builder.AddHeader(2, $"Downloaded using {nameof(DownloadedUsing.PRIMARY).ToTitleFromScreamingSnakeCase()}");

            builder.AddParagraph(
                $"Downloaded files using {nameof(DownloadedUsing.PRIMARY).ToTitleFromScreamingSnakeCase()}: {primary.Count}");

            builder.AddUnorderedList(ul =>
            {
                foreach (IDownloadTarget t in primary)
                {
                    string line = FormatSuccessLine(t.PrimaryLink, t.FullOutputFileName!, t.TimeToDownload, t.OutputFileSize);

                    ul.AddTaskListItem(line, true);
                }
            });
        }

        private void AddSecondarySection(MarkdownBuilder builder, IList<IDownloadTarget> secondary)
        {
            builder.AddHeader(2,
                $"Downloaded using {nameof(DownloadedUsing.SECONDARY).ToTitleFromScreamingSnakeCase()}");

            builder.AddParagraph(
                $"Downloaded files using {nameof(DownloadedUsing.SECONDARY).ToTitleFromScreamingSnakeCase()}: {secondary.Count}");

            builder.AddUnorderedList(ul =>
            {
                foreach (IDownloadTarget t in secondary)
                {
                    string line = FormatSuccessLine(t.SecondaryLink, t.FullOutputFileName!, t.TimeToDownload, t.OutputFileSize);

                    ul.AddTaskListItem(line, true);
                }
            });
        }

        private void AddNoneSection(MarkdownBuilder builder, IList<IDownloadTarget> none)
        {
            builder.AddHeader(2, "Not downloaded");

            builder.AddParagraph(
                $"Files not downloaded: {none.Count}");

            builder.AddUnorderedList(ul =>
            {
                foreach (IDownloadTarget t in none)
                    ul.AddTaskListItem(t.OutputFileName, false);
            });
        }

        private string FormatSuccessLine(string? link, string fullOutputFileName, TimeSpan? timeToDownload, long fileSizeBytes)
        {
            if (string.IsNullOrWhiteSpace(link))
                link = "<missing link>";

            string fileName = Path.GetFileName(fullOutputFileName);

            string timeText = FormatElapsed(timeToDownload);

            string sizeText = FormatFileSize(fileSizeBytes);

            return $"{timeText,10} {sizeText,6} {fileName,-12}  {link}";
        }

        private string FormatFileSize(long bytes)
        {
            const long KB = 1024;
            const long MB = KB * 1024;
            const long GB = MB * 1024;

            if (bytes >= GB)
                return $"{bytes / (double) GB:0}GB";

            if (bytes >= MB)
                return $"{bytes / (double) MB:0}MB";

            if (bytes >= KB)
                return $"{bytes / (double) KB:0}KB";

            return $"{bytes}B";
        }

        private static string FormatElapsed(TimeSpan? time)
        {
            if (time is null)
                return "unknown";

            var t = time.Value;

            if (t.TotalSeconds < 1)
                return $"{t.TotalMilliseconds:0}ms";

            if (t.TotalMinutes < 1)
                return $"{t.Seconds}s {t.Milliseconds}ms";

            return $"{(int) t.TotalMinutes}m {t.Seconds}s";
        }

        /// <inheritdoc />
        public string GetOutputFileExtension()
        {
            return ".md";
        }
    }
}