using Downloader.Abstraction.Enum;
using Downloader.Abstraction.Interfaces.Model;
using Downloader.Abstraction.Interfaces.Services;
using Downloader.Model;
using Downloader.Service;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Downloader.Tests.Service
{
    [TestFixture]
    public class LocalDriveFileServiceTests
    {
        private ILogger<LocalDriveFileService> logger = null!;
        private Mock<IInputReaderService> inputReaderMock = null!;
        private DownloaderSettings settings = null!;
        private IOptions<DownloaderSettings> options = null!;
        private LocalDriveFileService sut = null!;

        private string tempRoot = null!;
        private string downloadsDir = null!;
        private string reportsDir = null!;

        [SetUp]
        public void SetUp()
        {
            logger = Mock.Of<ILogger<LocalDriveFileService>>();
            inputReaderMock = new Mock<IInputReaderService>();

            tempRoot = Path.Combine(TestContext.CurrentContext.WorkDirectory, "LocalDriveFileServiceTests",
                Guid.NewGuid().ToString("N"));
            downloadsDir = Path.Combine(tempRoot, "downloads");
            reportsDir = Path.Combine(tempRoot, "reports");
            Directory.CreateDirectory(tempRoot);

            settings = new DownloaderSettings
            {
                FilesToDownloadExcelInput = Path.Combine(tempRoot, "input.xlsx"),
                DownloadedFilesOutputPath = downloadsDir,
                ReportsOutputPath = reportsDir,
                TargetStartIndex = -1,
                TargetEndIndex = -1,
            };

            options = Options.Create(settings);

            sut = new LocalDriveFileService(logger, inputReaderMock.Object, options);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, true);
            }
            catch
            {
                // best-effort cleanup
            }
        }

        #region LoadTargetsFromInput

        [Test]
        public async Task LoadTargetsFromInput_ReturnsTargetsFromInputReader()
        {
            // Arrange
            var expected = new List<IDownloadTarget>
            {
                CreateTarget(DownloadedUsing.PRIMARY, "A", "p", "s"),
                CreateTarget(DownloadedUsing.SECONDARY, "B", "p2", "s2"),
            };

            inputReaderMock.Setup(r => r.LoadTargets(settings.FilesToDownloadExcelInput)).ReturnsAsync(expected);

            // Act
            var result = await sut.LoadTargetsFromInput();

            // Assert
            result.Should().BeSameAs(expected);
            inputReaderMock.Verify(r => r.LoadTargets(settings.FilesToDownloadExcelInput), Times.Once);
        }

        #endregion

        #region ExportDownloadedFile

        [Test]
        public void ExportDownloadedFile_WhenTargetIsNull_ThrowsArgumentNullException()
        {
            // Arrange
            IDownloadTarget target = null!;
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("hello"));

            // Act
            var act = async () => await sut.ExportDownloadedFile(target, stream);

            // Assert
            act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("target");
        }

        [Test]
        public void ExportDownloadedFile_WhenFileNameMissing_ThrowsArgumentException()
        {
            // Arrange
            IDownloadTarget target = CreateTarget(DownloadedUsing.PRIMARY, "  ", "https://x/a.pdf");
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("hello"));

            // Act
            var act = async () => await sut.ExportDownloadedFile(target, stream);

            // Assert
            act.Should().ThrowAsync<ArgumentException>().WithParameterName("fileName");
        }

        [Test]
        public void ExportDownloadedFile_WhenStreamIsNull_ThrowsArgumentNullException()
        {
            // Arrange
            IDownloadTarget target = CreateTarget(DownloadedUsing.PRIMARY, "A", "https://x/a.pdf");
            Stream stream = null!;

            // Act
            var act = async () => await sut.ExportDownloadedFile(target, stream);

            // Assert
            act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("fileStream");
        }

        [Test]
        public void ExportDownloadedFile_WhenDownloadedUsingNone_ThrowsInvalidOperationException()
        {
            // Arrange
            IDownloadTarget target = CreateTarget(DownloadedUsing.NONE, "A", "https://x/a.pdf");
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("hello"));

            // Act
            var act = async () => await sut.ExportDownloadedFile(target, stream);

            // Assert
            act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Test]
        public async Task ExportDownloadedFile_PrimaryLink_WithNormalExtension_WritesFileAndSetsFullOutputFileName()
        {
            // Arrange
            IDownloadTarget target = CreateTarget(
                DownloadedUsing.PRIMARY, "BR50092", "https://example.com/files/report.pdf");

            byte[] bytes = Encoding.UTF8.GetBytes("content-123");
            using var stream = new MemoryStream(bytes);

            // Act
            await sut.ExportDownloadedFile(target, stream);

            // Assert
            Directory.Exists(downloadsDir).Should().BeTrue();

            target.FullOutputFilePath.Should().NotBeNullOrWhiteSpace();
            target.FullOutputFilePath.Should().EndWith(Path.Combine("downloads", "BR50092.pdf"));

            File.Exists(target.FullOutputFilePath).Should().BeTrue();
            byte[] written = await File.ReadAllBytesAsync(target.FullOutputFilePath);
            written.Should().Equal(bytes);
        }

        [Test]
        public async Task ExportDownloadedFile_ResetsStreamPosition_WhenNotZero()
        {
            // Arrange
            IDownloadTarget target = CreateTarget(
                DownloadedUsing.PRIMARY, "File", "https://example.com/files/data.txt");

            byte[] bytes = Encoding.UTF8.GetBytes("abcdef");
            using var stream = new MemoryStream(bytes);

            // Move position away from 0
            stream.Position = 3;

            // Act
            await sut.ExportDownloadedFile(target, stream);

            // Assert
            byte[] written = await File.ReadAllBytesAsync(target.FullOutputFilePath!);
            written.Should().Equal(bytes);
        }

        [Test]
        public async Task ExportDownloadedFile_SecondaryLink_UsesSecondaryLinkForExtension()
        {
            // Arrange
            IDownloadTarget target = CreateTarget(DownloadedUsing.SECONDARY, "X",
                "https://example.com/primary/wrong.docx", "https://example.com/secondary/right.xlsx");

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("hi"));

            // Act
            await sut.ExportDownloadedFile(target, stream);

            // Assert
            target.FullOutputFilePath.Should().EndWith(Path.Combine("downloads", "X.xlsx"));
        }

        [Test]
        public async Task ExportDownloadedFile_HandlesQueryFragmentAndHtmlEntities_WhenResolvingExtension()
        {
            // Arrange
            IDownloadTarget target = CreateTarget(DownloadedUsing.PRIMARY, "R",
                "https://example.com/report.pdf?la=en&amp;utm=abc#zoom=50");

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("data"));

            // Act
            await sut.ExportDownloadedFile(target, stream);

            // Assert
            target.FullOutputFilePath.Should().EndWith(Path.Combine("downloads", "R.pdf"));
        }

        [Test]
        public async Task ExportDownloadedFile_WrapperExtension_RecoversPreviousExtension()
        {
            // Arrange
            IDownloadTarget target = CreateTarget(DownloadedUsing.PRIMARY, "Report_2017",
                "https://example.com/Report_2017.pdf.aspx");

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("data"));

            // Act
            await sut.ExportDownloadedFile(target, stream);

            // Assert
            target.FullOutputFilePath.Should().EndWith(Path.Combine("downloads", "Report_2017.pdf"));
        }

        [Test]
        public async Task ExportDownloadedFile_CompressionExtension_BuildsDoubleExtensionWhenPossible()
        {
            // Arrange
            IDownloadTarget target = CreateTarget(
                DownloadedUsing.PRIMARY, "archive", "https://example.com/archive.tar.gz");

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("data"));

            // Act
            await sut.ExportDownloadedFile(target, stream);

            // Assert
            target.FullOutputFilePath.Should().EndWith(Path.Combine("downloads", "archive.tar.gz"));
        }

        [Test]
        public async Task ExportDownloadedFile_CompressionExtension_UsesCompressionOnlyWhenNoPreviousExtension()
        {
            // Arrange
            IDownloadTarget target = CreateTarget(DownloadedUsing.PRIMARY, "compressed", "https://example.com/file.gz");

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("data"));

            // Act
            await sut.ExportDownloadedFile(target, stream);

            // Assert
            target.FullOutputFilePath.Should().EndWith(Path.Combine("downloads", "compressed.gz"));
        }

        [Test]
        public async Task ExportDownloadedFile_NoExtensionInLink_WritesWithoutAppendingExtension()
        {
            // Arrange
            IDownloadTarget target = CreateTarget(DownloadedUsing.PRIMARY, "NoExt", "https://example.com/download");

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("data"));

            // Act
            await sut.ExportDownloadedFile(target, stream);

            // Assert
            target.FullOutputFilePath.Should().EndWith(Path.Combine("downloads", "NoExt"));
        }

        #endregion

        #region ExportReport

        [Test]
        public void ExportReport_WhenContentMissing_ThrowsArgumentException()
        {
            // Arrange
            var content = "   ";

            // Act
            var act = async () => await sut.ExportReport(content, "md");

            // Assert
            act.Should().ThrowAsync<ArgumentException>().WithParameterName("content");
        }

        [Test]
        public void ExportReport_WhenExtensionMissing_ThrowsArgumentException()
        {
            // Arrange
            var content = "hello";

            // Act
            var act = async () => await sut.ExportReport(content, "   ");

            // Assert
            act.Should().ThrowAsync<ArgumentException>().WithParameterName("fileExtension");
        }

        [Test]
        public void ExportReport_WhenReportsOutputPathNotConfigured_ThrowsInvalidOperationException()
        {
            // Arrange
            settings.ReportsOutputPath = "   ";
            sut = new LocalDriveFileService(logger, inputReaderMock.Object, Options.Create(settings));

            // Act
            var act = async () => await sut.ExportReport("hello", "md");

            // Assert
            act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ReportsOutputPath*");
        }

        [Test]
        public async Task ExportReport_WritesReportFile_WithTrimmedExtension_AndCorrectContent()
        {
            // Arrange
            var content = "# hello report";
            var ext = "  .md  ";

            // Act
            await sut.ExportReport(content, ext);

            // Assert
            Directory.Exists(reportsDir).Should().BeTrue();

            string[] reportFiles = Directory.GetFiles(reportsDir, "report-*.md");
            reportFiles.Should().HaveCount(1);

            string written = await File.ReadAllTextAsync(reportFiles.Single());
            written.Should().Be(content);
        }

        #endregion

        #region Helpers

        private static IDownloadTarget CreateTarget(
            DownloadedUsing downloadedUsing, string outputFileName, string? primaryLink = null,
            string? secondaryLink = null)
        {
            var mock = new Mock<IDownloadTarget>();

            mock.SetupGet(x => x.DownloadedUsing).Returns(downloadedUsing);
            mock.SetupGet(x => x.OutputFileName).Returns(outputFileName);
            mock.SetupGet(x => x.PrimaryLink).Returns(primaryLink);
            mock.SetupGet(x => x.SecondaryLink).Returns(secondaryLink);

            // service sets this property
            mock.SetupProperty(x => x.FullOutputFilePath, (string?) null);

            return mock.Object;
        }

        #endregion
    }
}