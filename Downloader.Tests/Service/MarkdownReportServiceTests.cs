using Downloader.Abstraction.Enum;
using Downloader.Abstraction.Interfaces.Model;
using Downloader.Model;
using Downloader.Service;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Downloader.Tests.Service
{
    [TestFixture]
    public class MarkdownReportServiceTests
    {
        private ILogger<MarkdownReportService> logger = null!;
        private Mock<IOptions<DownloaderSettings>> optionsMock = null!;
        private MarkdownReportService sut = null!;

        [SetUp]
        public void SetUp()
        {
            logger = Mock.Of<ILogger<MarkdownReportService>>();

            optionsMock = new Mock<IOptions<DownloaderSettings>>();
            optionsMock.Setup(o => o.Value).Returns(new DownloaderSettings
            {
                DownloadedFilesOutputPath = @"C:\Exports",
            });

            sut = new MarkdownReportService(logger, optionsMock.Object);
        }

        [Test]
        public void GenerateReport_WhenTargetsIsNull_ThrowsArgumentNullException()
        {
            // Arrange
            IList<IDownloadTarget>? targets = null;

            // Act
            var act = () => sut.GenerateReport(targets!, TimeSpan.FromMinutes(5));

            // Assert
            act.Should().Throw<ArgumentNullException>().WithParameterName("targets");
        }

        [Test]
        public void GenerateReport_WithMixedTargets_IncludesExpectedSectionsAndMetadata()
        {
            // Arrange
            var targets = new List<IDownloadTarget>
            {
                CreateTarget(DownloadedUsing.PRIMARY, "A", @"C:\Exports\a.txt", "https://primary/a", null,
                    TimeSpan.FromMilliseconds(150)),

                CreateTarget(DownloadedUsing.SECONDARY, "B", @"C:\Exports\b.txt", null, "https://secondary/b",
                    TimeSpan.FromMilliseconds(250)),

                CreateTarget(DownloadedUsing.NONE, "NoneFile", null, null, null, null),
            };

            // Act
            string report = sut.GenerateReport(targets, TimeSpan.FromMinutes(5));

            // Assert
            report.Should().Contain("# Download Report");
            report.Should().Contain("Created:");
            report.Should().Contain("Downloaded files output path: `C:\\Exports`");

            report.Should().Contain("## Downloaded using");
            report.Should().Contain("## Not downloaded");

            // Not downloaded section uses OutputFileName directly
            report.Should().Contain("NoneFile");
        }

        [Test]
        public void GenerateReport_PrimaryTarget_UsesPrimaryLink_FileNameAndMilliseconds()
        {
            // Arrange
            IDownloadTarget target = CreateTarget(DownloadedUsing.PRIMARY, "MyOut", @"C:\Exports\file1.zip",
                "https://primary/link", "https://secondary/link-should-not-be-used", TimeSpan.FromMilliseconds(1234));

            // Act
            string report = sut.GenerateReport(new List<IDownloadTarget> {target,}, TimeSpan.FromMinutes(5));

            // Assert
            report.Should().Contain("Downloaded using Primary");
            report.Should().Contain("file1.zip");
            report.Should().Contain("https://primary/link");
            report.Should().Contain("1s 234ms");
            report.Should().NotContain("https://secondary/link-should-not-be-used");
        }

        [Test]
        public void GenerateReport_SecondaryTarget_UsesSecondaryLink_FileNameAndMilliseconds()
        {
            // Arrange
            IDownloadTarget target = CreateTarget(DownloadedUsing.SECONDARY, "MyOut", @"C:\Exports\file2.zip",
                "https://primary/link-should-not-be-used", "https://secondary/link", TimeSpan.FromMilliseconds(777));

            // Act
            string report = sut.GenerateReport(new List<IDownloadTarget> {target,}, TimeSpan.FromMinutes(5));

            // Assert
            report.Should().Contain("Downloaded using Secondary");
            report.Should().Contain("file2.zip");
            report.Should().Contain("https://secondary/link");
            report.Should().Contain("777ms");
            report.Should().NotContain("https://primary/link-should-not-be-used");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void GenerateReport_WhenLinkMissing_UsesMissingLinkPlaceholder(string? missingLink)
        {
            // Arrange
            IDownloadTarget target = CreateTarget(DownloadedUsing.PRIMARY, "Out", @"C:\Exports\file3.zip", missingLink,
                null, TimeSpan.FromMilliseconds(10));

            // Act
            string report = sut.GenerateReport(new List<IDownloadTarget> {target,}, TimeSpan.FromMinutes(5));

            // Assert
            report.Should().Contain("<missing link>");
            report.Should().Contain("file3.zip");
        }

        [Test]
        public void GenerateReport_WhenTimeToDownloadMissing_UsesUnknown()
        {
            // Arrange
            IDownloadTarget target = CreateTarget(DownloadedUsing.PRIMARY, "Out", @"C:\Exports\file4.zip",
                "https://primary/link", null, null);

            // Act
            string report = sut.GenerateReport(new List<IDownloadTarget> {target,}, TimeSpan.FromMinutes(5));

            // Assert
            report.Should().Contain("unknown");
            report.Should().Contain("file4.zip");
            report.Should().Contain("https://primary/link");
        }

        [Test]
        public void GetOutputFileExtension_ReturnsMd()
        {
            // Arrange / Act
            string ext = sut.GetOutputFileExtension();

            // Assert
            ext.Should().Be(".md");
        }

        private static IDownloadTarget CreateTarget(
            DownloadedUsing downloadedUsing, string outputFileName, string? fullOutputFileName, string? primaryLink,
            string? secondaryLink, TimeSpan? timeToDownload)
        {
            var mock = new Mock<IDownloadTarget>();

            mock.SetupGet(x => x.DownloadedUsing).Returns(downloadedUsing);
            mock.SetupGet(x => x.OutputFileName).Returns(outputFileName);
            mock.SetupGet(x => x.FullOutputFilePath).Returns(fullOutputFileName);
            mock.SetupGet(x => x.PrimaryLink).Returns(primaryLink);
            mock.SetupGet(x => x.SecondaryLink).Returns(secondaryLink);
            mock.SetupGet(x => x.TimeToDownload).Returns(timeToDownload);

            return mock.Object;
        }
    }
}