using System.Diagnostics;
using Downloader.Abstraction.Interfaces.Model;
using Downloader.Abstraction.Interfaces.Services;
using Downloader.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Downloader.Service
{
    /// FEEDBACK
    /// S: SplitTargetsOnLinkExistence: Should Orchestrator be responsible for dividing valid and not valid links?
    /// S: Could maybe also consider a service to take care of the queue
    /// O:
    /// L:
    /// I:
    /// D:
    /// Naming: Async Methods should be named with Async in the end
    /// Readability: TargetCounts seems unnecessarily complex
    /// Organisation: I like that there is many private methods to make the main public more readable
    /// Comments: Use of readable methods makes comments unnecessary
    /// Error Handling: No error handling at this level. Could be last ditch handling?
    /// Logging: Lots of logging. Hard to say if more is needed. (If there was more error handling)
    /// Test Ideas:
    /// Other:
    public class OrchestratorService : IOrchestratorService
    {
        private readonly ILogger<OrchestratorService> logger;
        private readonly IFileService fileService;
        private readonly IDownloadService downloadService;
        private readonly IReportService reportService;
        private readonly IOptions<DownloaderSettings> options;
        private TimeSpan? timeToDownload = null;

        public OrchestratorService(
            ILogger<OrchestratorService> logger, IFileService fileService, IDownloadService downloadService,
            IReportService reportService, IOptions<DownloaderSettings> options)
        {
            this.logger = logger;
            this.fileService = fileService;
            this.downloadService = downloadService;
            this.reportService = reportService;
            this.options = options;
        }

        /// <inheritdoc />
        public async Task InitiateWorkflow()
        {
            logger.LogInformation("Workflow started.");

            var (validTargets, invalidTargets) = await LoadTargets();
            TargetCounts counts = GetCounts(validTargets, invalidTargets);

            if (ShouldExitWithoutReport(counts))
            {
                logger.LogInformation("Workflow completed with no targets and no report.");
                return;
            }

            if (ShouldGenerateInvalidOnlyReport(counts))
            {
                await GenerateAndExportInvalidOnlyReport(invalidTargets, counts);
                return;
            }

            var downloadedTargets = await DownloadWithConcurrencyLimit(validTargets,
                options.Value.MaxConcurrentDownloads);
            var targetsForReport = MergeForReport(downloadedTargets, invalidTargets);

            await GenerateAndExportReport(targetsForReport);

            logger.LogInformation(
                "Workflow completed. Downloaded: {DownloadedCount}, Invalid: {InvalidCount}, Total: {TotalCount}",
                downloadedTargets.Count, invalidTargets.Count, targetsForReport.Count);
        }

        private sealed record TargetCounts(int Valid, int Invalid)
        {
            public int Total => Valid + Invalid;
        }

        private TargetCounts GetCounts(IList<IDownloadTarget> valid, IList<IDownloadTarget> invalid)
        {
            return new TargetCounts(valid.Count, invalid.Count);
        }

        private bool ShouldExitWithoutReport(TargetCounts counts)
        {
            return counts.Total == 0;
        }

        private bool ShouldGenerateInvalidOnlyReport(TargetCounts counts)
        {
            return counts.Valid == 0 && counts.Invalid > 0;
        }

        private async Task GenerateAndExportInvalidOnlyReport(
            IList<IDownloadTarget> invalidTargets, TargetCounts counts)
        {
            logger.LogWarning(
                "Workflow completed with no valid targets to download. Generating report for {InvalidCount} invalid targets.",
                counts.Invalid);

            await GenerateAndExportReport(invalidTargets);

            logger.LogInformation("Workflow completed. Downloaded: 0, Invalid: {InvalidCount}", counts.Invalid);
        }

        private IList<IDownloadTarget> MergeForReport(
            IList<IDownloadTarget> downloadedTargets, IList<IDownloadTarget> invalidTargets)
        {
            var merged = new List<IDownloadTarget>(downloadedTargets.Count + invalidTargets.Count);
            merged.AddRange(downloadedTargets);
            merged.AddRange(invalidTargets);

            merged.Sort((a, b) =>
                string.Compare(a.OutputFileName, b.OutputFileName, StringComparison.OrdinalIgnoreCase));

            return merged;
        }

        private async Task GenerateAndExportReport(IList<IDownloadTarget> targetsForReport)
        {
            string report = reportService.GenerateReport(targetsForReport, timeToDownload);
            await fileService.ExportReport(report, reportService.GetOutputFileExtension());
        }

        private async Task<(IList<IDownloadTarget> validTargets, IList<IDownloadTarget> invalidTargets)> LoadTargets()
        {
            var targets = await fileService.LoadTargetsFromInput();
            return SplitTargetsOnLinkExistence(targets);
        }

        private (IList<IDownloadTarget> validTargets, IList<IDownloadTarget> invalidTargets)
            SplitTargetsOnLinkExistence(IList<IDownloadTarget> targets)
        {
            if (targets.Count == 0)
                return (targets, targets);

            var hasLink = new List<IDownloadTarget>(targets.Count);
            var doesNotHaveLink = new List<IDownloadTarget>(targets.Count);

            foreach (IDownloadTarget target in targets)
            {
                if (HasAnyLink(target))
                {
                    hasLink.Add(target);
                    continue;
                }

                doesNotHaveLink.Add(target);
                logger.LogWarning(
                    "Removing target '{OutputFileName}' because neither {PrimaryLink} nor {SecondaryLink} is set.",
                    target.OutputFileName, nameof(IDownloadTarget.PrimaryLink), nameof(IDownloadTarget.SecondaryLink));
            }

            return (hasLink, doesNotHaveLink);
        }

        private bool HasAnyLink(IDownloadTarget target)
        {
            return !string.IsNullOrWhiteSpace(target.PrimaryLink) || !string.IsNullOrWhiteSpace(target.SecondaryLink);
        }

        private async Task<IList<IDownloadTarget>> DownloadWithConcurrencyLimit(
            IList<IDownloadTarget> targets, int maxConcurrentDownloads)
        {
            var workQueue = BuildWorkQueue(targets);
            return await RunQueueWithLimit(workQueue, maxConcurrentDownloads);
        }

        private Queue<Func<Task<IDownloadTarget>>> BuildWorkQueue(IList<IDownloadTarget> targets)
        {
            var queue = new Queue<Func<Task<IDownloadTarget>>>(targets.Count);

            foreach (IDownloadTarget target in targets)
            {
                IDownloadTarget captured = target;
                queue.Enqueue(() => downloadService.DownloadContent(captured));
            }

            return queue;
        }

        private async Task<IList<IDownloadTarget>> RunQueueWithLimit(
            Queue<Func<Task<IDownloadTarget>>> queue, int maxConcurrent)
        {
            var total = queue.Count;
            var active = new List<Task<IDownloadTarget>>(Math.Min(maxConcurrent, queue.Count));
            var completed = new List<IDownloadTarget>();

            TimeSpan? estimatedRemaining = null;
            TimeSpan? averageEstimatedRemaining = null;
            long etaTicksSum = 0;
            int etaSamples = 0;

            var sw = Stopwatch.StartNew();

            StartInitialBatch(queue, active, maxConcurrent);

            logger.LogInformation("Download progress started. Completed: 0, Remaining: {Remaining}, Total: {Total}, MaxConcurrent: {MaxConcurrent}",
                total, total, maxConcurrent);

            while (active.Count > 0)
            {
                var finished = await Task.WhenAny(active);
                active.Remove(finished);

                // Fail-fast: exceptions bubbles up here
                completed.Add(await finished);

                var completedCount = completed.Count;
                var remainingCount = total - completedCount;

                if (completedCount > 0)
                {
                    estimatedRemaining = TimeSpan.FromTicks(sw.Elapsed.Ticks / completedCount * remainingCount);

                    etaTicksSum += estimatedRemaining.Value.Ticks;
                    etaSamples++;

                    averageEstimatedRemaining = TimeSpan.FromTicks(etaTicksSum / etaSamples);
                }

                logger.LogInformation("Download progress. Completed: {Completed}, Remaining: {Remaining}", completedCount, remainingCount);
                LogProgressBar(completedCount, total);
                logger.LogInformation("Time so far: {Time}, Estimated Time Left: {Left}", sw.Elapsed,
                    averageEstimatedRemaining.HasValue ? estimatedRemaining.ToString() : "Unknown");

                StartNextIfAvailable(queue, active);
            }

            sw.Stop();
            logger.LogInformation("Download of {Total} targets completed in {Time}", total, sw.Elapsed);
            timeToDownload = sw.Elapsed;

            return completed;
        }

        private void LogProgressBar(int completed, int total, int barWidth = 30)
        {
            if (total <= 0)
            {
                logger.LogInformation("[{Bar}] {Percent}% ({Completed}/{Total})", new string('░', barWidth), 0,
                    completed, total);

                return;
            }

            var progress = (double) completed / total;
            var filled = (int) Math.Round(progress * barWidth);

            var bar = new string('█', filled) + new string('░', barWidth - filled);

            var percent = Math.Round(progress * 100, 1);

            logger.LogInformation("[{Bar}] {Percent}% ({Completed}/{Total})", bar, percent, completed, total);
        }

        private void StartInitialBatch(
            Queue<Func<Task<IDownloadTarget>>> queue, List<Task<IDownloadTarget>> active, int maxConcurrent)
        {
            while (active.Count < maxConcurrent && queue.Count > 0)
                active.Add(queue.Dequeue().Invoke());
        }

        private void StartNextIfAvailable(Queue<Func<Task<IDownloadTarget>>> queue, List<Task<IDownloadTarget>> active)
        {
            if (queue.Count > 0)
                active.Add(queue.Dequeue().Invoke());
        }
    }
}