using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Downloader.Abstraction.Interfaces.Model;
using Downloader.Abstraction.Interfaces.Services;
using Downloader.Model;
using Downloader.Service;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace Downloader.Tests.Service
{
    [TestFixture]
    public class OrchestratorServiceTests
    {
        private ILogger<OrchestratorService> logger = null!;
        private Mock<IFileService> fileServiceMock = null!;
        private Mock<IDownloadService> downloadServiceMock = null!;
        private Mock<IReportService> reportServiceMock = null!;

        [SetUp]
        public void SetUp()
        {
            logger = Mock.Of<ILogger<OrchestratorService>>();
            fileServiceMock = new Mock<IFileService>();
            downloadServiceMock = new Mock<IDownloadService>();
            reportServiceMock = new Mock<IReportService>();
        }

        [Test]
        public async Task InitiateWorkflow_WhenNoTargets_ExitsWithoutReportAndWithoutDownloads()
        {
            // Arrange
            fileServiceMock.Setup(fs => fs.LoadTargetsFromInput()).ReturnsAsync(new List<IDownloadTarget>());

            OrchestratorService sut = CreateSut(2);

            // Act
            await sut.InitiateWorkflow();

            // Assert
            downloadServiceMock.Verify(ds => ds.DownloadContent(It.IsAny<IDownloadTarget>()), Times.Never);
            reportServiceMock.Verify(rs => rs.GenerateReport(It.IsAny<IList<IDownloadTarget>>(), It.IsAny<TimeSpan?>()), Times.Never);
            fileServiceMock.Verify(fs => fs.ExportReport(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task InitiateWorkflow_WhenOnlyInvalidTargets_GeneratesAndExportsReport_WithoutDownloading()
        {
            // Arrange
            var invalidTargets = GivenInvalidTargets(("B", null, null), ("A", "   ", "  "));

            GivenInputTargets(invalidTargets);
            GivenReportOutputs("REPORT", ".md");

            OrchestratorService sut = CreateSut(2);

            // Act
            await sut.InitiateWorkflow();

            // Assert
            ThenNoDownloadsWereStarted();
            ThenReportWasGeneratedOnce();
            ThenReportWasExported("REPORT", ".md");
        }

        [Test]
        public async Task InitiateWorkflow_WhenValidAndInvalidTargets_DownloadsValid_MergesSortsAndExportsReport()
        {
            // Arrange
            IDownloadTarget tB = GivenTarget("b", "https://primary/b", null);
            IDownloadTarget tA = GivenTarget("A", null, "https://secondary/a");
            IDownloadTarget tC = GivenTarget("c", "   ", null);

            var inputTargets = new List<IDownloadTarget> {tB, tC, tA,};

            GivenInputTargets(inputTargets);
            GivenDownloadServiceReturnsSameTarget();
            ReportTargetsCapture capture = GivenReportOutputsAndCaptureTargets("REPORT", ".md");

            OrchestratorService sut = CreateSut(5);

            // Act
            await sut.InitiateWorkflow();

            // Assert
            ThenTargetWasDownloaded(tA);
            ThenTargetWasDownloaded(tB);
            ThenTargetWasNotDownloaded(tC);

            ThenReportWasExported("REPORT", ".md");

            // Merge + sort by OutputFileName (case-insensitive): A, b, c
            capture.Value.Should().NotBeNull();
            capture.Value!.Select(t => t.OutputFileName).Should().Equal("A", "b", "c");
        }

        [Test]
        public async Task InitiateWorkflow_RespectsMaxConcurrentDownloads()
        {
            // Arrange
            IDownloadTarget t1 = GivenTarget("T1", "https://x/1", null);
            IDownloadTarget t2 = GivenTarget("T2", "https://x/2", null);
            IDownloadTarget t3 = GivenTarget("T3", "https://x/3", null);

            GivenInputTargets(new List<IDownloadTarget> {t1, t2, t3,});
            GivenReportOutputs("REPORT", ".md");

            ConcurrencyLimiter limiter = GivenConcurrencyControlledDownloads(t1, t2, t3);

            OrchestratorService sut = CreateSut(2);

            // Act
            Task workflowTask = sut.InitiateWorkflow();

            // Assert
            await limiter.ThenWaitForInitialStarts(2);

            limiter.Complete(t1);

            await limiter.ThenWaitForInitialStarts(3);

            limiter.Complete(t2);
            limiter.Complete(t3);

            await workflowTask;

            ThenReportWasExported("REPORT", ".md");
        }

        #region Helpers

        private OrchestratorService CreateSut(int maxConcurrentDownloads)
        {
            var options = Options.Create(new DownloaderSettings
            {
                MaxConcurrentDownloads = maxConcurrentDownloads,
            });

            return new OrchestratorService(logger, fileServiceMock.Object, downloadServiceMock.Object,
                reportServiceMock.Object, options);
        }

        private void GivenInputTargets(IList<IDownloadTarget> targets)
        {
            fileServiceMock.Setup(fs => fs.LoadTargetsFromInput()).ReturnsAsync(targets);
        }

        private static IList<IDownloadTarget> GivenInvalidTargets(
            params (string name, string? primary, string? secondary)[] specs)
        {
            return specs.Select(s => GivenTarget(s.name, s.primary, s.secondary)).ToList();
        }

        private static IDownloadTarget GivenTarget(string outputFileName, string? primary, string? secondary)
        {
            var mock = new Mock<IDownloadTarget>();
            mock.SetupGet(t => t.OutputFileName).Returns(outputFileName);
            mock.SetupGet(t => t.PrimaryLink).Returns(primary);
            mock.SetupGet(t => t.SecondaryLink).Returns(secondary);
            return mock.Object;
        }

        private void GivenReportOutputs(string report, string extension)
        {
            reportServiceMock.Setup(rs => rs.GenerateReport(It.IsAny<IList<IDownloadTarget>>(), It.IsAny<TimeSpan?>())).Returns(report);
            reportServiceMock.Setup(rs => rs.GetOutputFileExtension()).Returns(extension);
        }

        private sealed class ReportTargetsCapture
        {
            public IList<IDownloadTarget>? Value { get; set; }
        }

        private ReportTargetsCapture GivenReportOutputsAndCaptureTargets(string report, string extension)
        {
            var capture = new ReportTargetsCapture();

            reportServiceMock.Setup(rs => rs.GenerateReport(It.IsAny<IList<IDownloadTarget>>(), It.IsAny<TimeSpan?>()))
                .Callback<IList<IDownloadTarget>, TimeSpan?>((list, _) => capture.Value = list).Returns(report);

            reportServiceMock.Setup(rs => rs.GetOutputFileExtension()).Returns(extension);

            return capture;
        }

        private void GivenDownloadServiceReturnsSameTarget()
        {
            downloadServiceMock.Setup(ds => ds.DownloadContent(It.IsAny<IDownloadTarget>()))
                .Returns<IDownloadTarget>(t => Task.FromResult(t));
        }

        private void ThenNoDownloadsWereStarted()
        {
            downloadServiceMock.Verify(ds => ds.DownloadContent(It.IsAny<IDownloadTarget>()), Times.Never);
        }

        private void ThenReportWasGeneratedOnce()
        {
            reportServiceMock.Verify(rs => rs.GenerateReport(It.IsAny<IList<IDownloadTarget>>(), It.IsAny<TimeSpan?>()), Times.Once);
        }

        private void ThenReportWasExported(string content, string extension)
        {
            fileServiceMock.Verify(fs => fs.ExportReport(content, extension), Times.Once);
        }

        private void ThenTargetWasDownloaded(IDownloadTarget target)
        {
            downloadServiceMock.Verify(ds => ds.DownloadContent(target), Times.Once);
        }

        private void ThenTargetWasNotDownloaded(IDownloadTarget target)
        {
            downloadServiceMock.Verify(ds => ds.DownloadContent(target), Times.Never);
        }

        // ---- Concurrency helper ----

        private ConcurrencyLimiter GivenConcurrencyControlledDownloads(params IDownloadTarget[] targets)
        {
            var limiter = new ConcurrencyLimiter(targets);

            downloadServiceMock.Setup(ds => ds.DownloadContent(It.IsAny<IDownloadTarget>()))
                .Returns<IDownloadTarget>(target =>
                {
                    limiter.MarkStarted();
                    return limiter.GetTaskFor(target);
                });

            return limiter;
        }

        private sealed class ConcurrencyLimiter
        {
            private int startedCount;
            private readonly Dictionary<IDownloadTarget, TaskCompletionSource<IDownloadTarget>> completions;

            public ConcurrencyLimiter(IEnumerable<IDownloadTarget> targets)
            {
                completions = targets.ToDictionary(t => t,
                    _ => new TaskCompletionSource<IDownloadTarget>(TaskCreationOptions.RunContinuationsAsynchronously));
            }

            public void MarkStarted()
            {
                Interlocked.Increment(ref startedCount);
            }

            public Task<IDownloadTarget> GetTaskFor(IDownloadTarget target)
            {
                return completions[target].Task;
            }

            public void Complete(IDownloadTarget target)
            {
                completions[target].SetResult(target);
            }

            public async Task ThenWaitForInitialStarts(int expectedStarted)
            {
                // Small polling loop is more robust than a single delay.
                // Keeps the test stable if the runner is fast/slow.
                DateTime timeout = DateTime.UtcNow.AddSeconds(3);

                while (Volatile.Read(ref startedCount) < expectedStarted)
                {
                    if (DateTime.UtcNow > timeout)
                        throw new TimeoutException(
                            $"Timed out waiting for startedCount to reach {expectedStarted}. Current: {startedCount}");

                    await Task.Delay(20);
                }

                Volatile.Read(ref startedCount).Should().Be(expectedStarted);
            }
        }

        #endregion
    }
}