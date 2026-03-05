using Downloader.Model;
using FluentAssertions;

namespace Downloader.Tests.Model
{
    [TestFixture]
    public class DownloaderSettingsTests
    {
        private static DownloaderSettings CreateValidSettings()
        {
            var rootedBase = Path.Combine(Path.GetTempPath(), "DownloaderSettingsTests");

            return new DownloaderSettings
            {
                DownloadedFilesOutputPath = Path.Combine(rootedBase, "Downloads"),
                ReportsOutputPath = Path.Combine(rootedBase, "Reports"),
                FilesToDownloadExcelInput = Path.Combine(rootedBase, "input.xlsx"),

                MaxConcurrentDownloads = 5,
                DownloadRetries = 3,
                SecondsWaitBetweenRetry = 5,

                TargetStartIndex = -1,
                TargetEndIndex = -1
            };
        }

        [Test]
        public void IsValid_WhenSettingsIsNull_ReturnsFalse()
        {
            // Arrange
            DownloaderSettings? settings = null;

            // Act
            var result = DownloaderSettings.IsValid(settings);

            // Assert
            result.Should().BeFalse();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void IsValid_WhenDownloadedFilesOutputPathIsMissing_ReturnsFalse(string? value)
        {
            // Arrange
            var settings = CreateValidSettings();
            settings.DownloadedFilesOutputPath = value!;

            // Act
            var result = DownloaderSettings.IsValid(settings);

            // Assert
            result.Should().BeFalse();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void IsValid_WhenReportsOutputPathIsMissing_ReturnsFalse(string? value)
        {
            // Arrange
            var settings = CreateValidSettings();
            settings.ReportsOutputPath = value!;

            // Act
            var result = DownloaderSettings.IsValid(settings);

            // Assert
            result.Should().BeFalse();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void IsValid_WhenFilesToDownloadExcelInputIsMissing_ReturnsFalse(string? value)
        {
            // Arrange
            var settings = CreateValidSettings();
            settings.FilesToDownloadExcelInput = value!;

            // Act
            var result = DownloaderSettings.IsValid(settings);

            // Assert
            result.Should().BeFalse();
        }

        [Test]
        public void IsValid_WhenDownloadedFilesOutputPathIsNotRooted_ReturnsFalse()
        {
            // Arrange
            var settings = CreateValidSettings();
            settings.DownloadedFilesOutputPath = "relative-path";

            // Act
            var result = DownloaderSettings.IsValid(settings);

            // Assert
            result.Should().BeFalse();
        }

        [Test]
        public void IsValid_WhenReportsOutputPathIsNotRooted_ReturnsFalse()
        {
            // Arrange
            var settings = CreateValidSettings();
            settings.ReportsOutputPath = "relative-path";

            // Act
            var result = DownloaderSettings.IsValid(settings);

            // Assert
            result.Should().BeFalse();
        }

        [Test]
        public void IsValid_WhenFilesToDownloadExcelInputIsNotRooted_ReturnsFalse()
        {
            // Arrange
            var settings = CreateValidSettings();
            settings.FilesToDownloadExcelInput = "relative.xlsx";

            // Act
            var result = DownloaderSettings.IsValid(settings);

            // Assert
            result.Should().BeFalse();
        }

        [Test]
        public void IsValid_WhenMaxConcurrentDownloadsIsLessThan1_ReturnsFalse()
        {
            // Arrange
            var settings = CreateValidSettings();
            settings.MaxConcurrentDownloads = 0;

            // Act
            var result = DownloaderSettings.IsValid(settings);

            // Assert
            result.Should().BeFalse();
        }

        [Test]
        public void IsValid_WhenDownloadRetriesIsNegative_ReturnsFalse()
        {
            // Arrange
            var settings = CreateValidSettings();
            settings.DownloadRetries = -1;

            // Act
            var result = DownloaderSettings.IsValid(settings);

            // Assert
            result.Should().BeFalse();
        }

        [Test]
        public void IsValid_WhenSecondsWaitBetweenRetryIsNegative_ReturnsFalse()
        {
            // Arrange
            var settings = CreateValidSettings();
            settings.SecondsWaitBetweenRetry = -1;

            // Act
            var result = DownloaderSettings.IsValid(settings);

            // Assert
            result.Should().BeFalse();
        }

        [Test]
        public void IsValid_WhenAllRequiredValuesAreValid_ReturnsTrue()
        {
            // Arrange
            var settings = CreateValidSettings();

            // Act
            var result = DownloaderSettings.IsValid(settings);

            // Assert
            result.Should().BeTrue();
        }

        [Test]
        public void IsValid_NormalizesTargetBounds_WhenTheyAreLessThanMinusOne()
        {
            // Arrange
            var settings = CreateValidSettings();
            settings.TargetStartIndex = -2;
            settings.TargetEndIndex = -123;

            // Force IsValid to return false *after* normalization so we can verify side-effects.
            settings.MaxConcurrentDownloads = 0;

            // Act
            var result = DownloaderSettings.IsValid(settings);

            // Assert
            result.Should().BeFalse();
            settings.TargetStartIndex.Should().Be(-1);
            settings.TargetEndIndex.Should().Be(-1);
        }

        [Test]
        public void IsValid_DoesNotChangeTargetBounds_WhenTheyAreMinusOneOrGreater()
        {
            // Arrange
            var settings = CreateValidSettings();
            settings.TargetStartIndex = -1;
            settings.TargetEndIndex = 10;

            // Act
            var result = DownloaderSettings.IsValid(settings);

            // Assert
            result.Should().BeTrue();
            settings.TargetStartIndex.Should().Be(-1);
            settings.TargetEndIndex.Should().Be(10);
        }

        [Test]
        public void IsValid_WhenFilesToDownloadExcelInputIsNotAnExcelExtension_ReturnsFalse()
        {
            // Arrange
            var settings = CreateValidSettings();
            settings.FilesToDownloadExcelInput = Path.Combine(Path.GetTempPath(), "not-excel.txt");

            // Act
            var result = DownloaderSettings.IsValid(settings);

            // Assert
            result.Should().BeFalse();
        }

        [TestCase("input.xlsx")]
        [TestCase("input.xlsm")]
        [TestCase("input.xls")]
        [TestCase("input.xlsb")]
        [TestCase("input.xltx")]
        [TestCase("input.xltm")]
        public void IsValid_WhenFilesToDownloadExcelInputHasExcelExtension_ReturnsTrue(string fileName)
        {
            // Arrange
            var settings = CreateValidSettings();
            settings.FilesToDownloadExcelInput = Path.Combine(Path.GetTempPath(), fileName);

            // Act
            var result = DownloaderSettings.IsValid(settings);

            // Assert
            result.Should().BeTrue();
        }
    }
}