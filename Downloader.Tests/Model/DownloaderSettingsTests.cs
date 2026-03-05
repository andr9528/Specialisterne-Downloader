using Downloader.Model;
using FluentAssertions;

namespace Downloader.Tests.Model
{
    [TestFixture]
    public class DownloaderSettingsTests
    {
        private static DownloaderSettings CreateValidSettings()
        {
            string rootedBase = Path.Combine(Path.GetTempPath(), "DownloaderSettingsTests");

            return new DownloaderSettings
            {
                DownloadedFilesOutputPath = Path.Combine(rootedBase, "Downloads"),
                ReportsOutputPath = Path.Combine(rootedBase, "Reports"),
                FilesToDownloadExcelInput = Path.Combine(rootedBase, "input.xlsx"),

                MaxConcurrentDownloads = 5,
                DownloadRetries = 3,
                SecondsWaitBetweenRetry = 5,

                TargetStartIndex = -1,
                TargetEndIndex = -1,
            };
        }

        [Test]
        public void IsValid_WhenSettingsIsNull_ReturnsFalse()
        {
            // Arrange
            DownloaderSettings? settings = null;

            // Act
            bool result = DownloaderSettings.IsValid(settings);

            // Assert
            result.Should().BeFalse();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void IsValid_WhenDownloadedFilesOutputPathIsMissing_ReturnsFalse(string? value)
        {
            // Arrange
            DownloaderSettings settings = CreateValidSettings();
            settings.DownloadedFilesOutputPath = value!;

            // Act
            bool result = DownloaderSettings.IsValid(settings);

            // Assert
            result.Should().BeFalse();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void IsValid_WhenReportsOutputPathIsMissing_ReturnsFalse(string? value)
        {
            // Arrange
            DownloaderSettings settings = CreateValidSettings();
            settings.ReportsOutputPath = value!;

            // Act
            bool result = DownloaderSettings.IsValid(settings);

            // Assert
            result.Should().BeFalse();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void IsValid_WhenFilesToDownloadExcelInputIsMissing_ReturnsFalse(string? value)
        {
            // Arrange
            DownloaderSettings settings = CreateValidSettings();
            settings.FilesToDownloadExcelInput = value!;

            // Act
            bool result = DownloaderSettings.IsValid(settings);

            // Assert
            result.Should().BeFalse();
        }

        [Test]
        public void IsValid_WhenDownloadedFilesOutputPathIsNotRooted_ReturnsFalse()
        {
            // Arrange
            DownloaderSettings settings = CreateValidSettings();
            settings.DownloadedFilesOutputPath = "relative-path";

            // Act
            bool result = DownloaderSettings.IsValid(settings);

            // Assert
            result.Should().BeFalse();
        }

        [Test]
        public void IsValid_WhenReportsOutputPathIsNotRooted_ReturnsFalse()
        {
            // Arrange
            DownloaderSettings settings = CreateValidSettings();
            settings.ReportsOutputPath = "relative-path";

            // Act
            bool result = DownloaderSettings.IsValid(settings);

            // Assert
            result.Should().BeFalse();
        }

        [Test]
        public void IsValid_WhenFilesToDownloadExcelInputIsNotRooted_ReturnsFalse()
        {
            // Arrange
            DownloaderSettings settings = CreateValidSettings();
            settings.FilesToDownloadExcelInput = "relative.xlsx";

            // Act
            bool result = DownloaderSettings.IsValid(settings);

            // Assert
            result.Should().BeFalse();
        }

        [Test]
        public void IsValid_WhenMaxConcurrentDownloadsIsLessThan1_ReturnsFalse()
        {
            // Arrange
            DownloaderSettings settings = CreateValidSettings();
            settings.MaxConcurrentDownloads = 0;

            // Act
            bool result = DownloaderSettings.IsValid(settings);

            // Assert
            result.Should().BeFalse();
        }

        [Test]
        public void IsValid_WhenDownloadRetriesIsNegative_ReturnsFalse()
        {
            // Arrange
            DownloaderSettings settings = CreateValidSettings();
            settings.DownloadRetries = -1;

            // Act
            bool result = DownloaderSettings.IsValid(settings);

            // Assert
            result.Should().BeFalse();
        }

        [Test]
        public void IsValid_WhenSecondsWaitBetweenRetryIsNegative_ReturnsFalse()
        {
            // Arrange
            DownloaderSettings settings = CreateValidSettings();
            settings.SecondsWaitBetweenRetry = -1;

            // Act
            bool result = DownloaderSettings.IsValid(settings);

            // Assert
            result.Should().BeFalse();
        }

        [Test]
        public void IsValid_WhenAllRequiredValuesAreValid_ReturnsTrue()
        {
            // Arrange
            DownloaderSettings settings = CreateValidSettings();

            // Act
            bool result = DownloaderSettings.IsValid(settings);

            // Assert
            result.Should().BeTrue();
        }

        [Test]
        public void IsValid_NormalizesTargetBounds_WhenTheyAreLessThanMinusOne()
        {
            // Arrange
            DownloaderSettings settings = CreateValidSettings();
            settings.TargetStartIndex = -2;
            settings.TargetEndIndex = -123;

            // Force IsValid to return false *after* normalization so we can verify side-effects.
            settings.MaxConcurrentDownloads = 0;

            // Act
            bool result = DownloaderSettings.IsValid(settings);

            // Assert
            result.Should().BeFalse();
            settings.TargetStartIndex.Should().Be(-1);
            settings.TargetEndIndex.Should().Be(-1);
        }

        [Test]
        public void IsValid_DoesNotChangeTargetBounds_WhenTheyAreMinusOneOrGreater()
        {
            // Arrange
            DownloaderSettings settings = CreateValidSettings();
            settings.TargetStartIndex = -1;
            settings.TargetEndIndex = 10;

            // Act
            bool result = DownloaderSettings.IsValid(settings);

            // Assert
            result.Should().BeTrue();
            settings.TargetStartIndex.Should().Be(-1);
            settings.TargetEndIndex.Should().Be(10);
        }

        [Test]
        public void IsValid_WhenFilesToDownloadExcelInputIsNotAnExcelExtension_ReturnsFalse()
        {
            // Arrange
            DownloaderSettings settings = CreateValidSettings();
            settings.FilesToDownloadExcelInput = Path.Combine(Path.GetTempPath(), "not-excel.txt");

            // Act
            bool result = DownloaderSettings.IsValid(settings);

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
            DownloaderSettings settings = CreateValidSettings();
            settings.FilesToDownloadExcelInput = Path.Combine(Path.GetTempPath(), fileName);

            // Act
            bool result = DownloaderSettings.IsValid(settings);

            // Assert
            result.Should().BeTrue();
        }
    }
}