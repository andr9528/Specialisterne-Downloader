namespace Downloader.Model
{
    public class DownloaderSettings
    {
        /// <summary>
        /// Full path to location where generated reports will be placed.
        /// </summary>
        public string ReportsOutputPath { get; set; }

        /// <summary>
        /// Full path to location where downloaded files will be placed.
        /// </summary>
        public string DownloadedFilesOutputPath { get; set; }

        /// <summary>
        /// Full path to Excel file containing the links to be downloaded / checked.
        /// Expects the following columns to contain...
        /// <list type="bullet">
        /// <item><description>A: Name of output downloaded file.</description></item>
        /// <item><description>AL: Primary download Link.</description></item>
        /// <item><description>AM: Secondary download Link.</description></item>
        /// </list>
        /// </summary>
        public string FilesToDownloadExcelInput { get; set; }

        /// <summary>
        /// Maximum amount of created threads, to split download workload on.
        /// Suggested value: 3 - 10
        /// </summary>
        public int MaxConcurrentDownloads { get; set; } = 5;

        /// <summary>
        /// Maximum retries using Primary or Secondary link, before marking link dead.
        /// Suggested value: 1 - 5
        /// </summary>
        public int DownloadRetries { get; set; } = 3;

        /// <summary>
        /// Seconds waited between download retries.
        /// Suggested value: 1 - 15
        /// </summary>
        public int SecondsWaitBetweenRetry { get; set; } = 5;

        /// <summary>
        /// Defines the lower inclusive index bound of the targets to generate.
        /// 
        /// A value of -1 means no lower limit (start from the first target).
        /// 
        /// Example:
        ///     0  -> start from first target
        ///     10 -> start from the 11th target
        ///     -1 -> no lower bound
        /// </summary>
        public int TargetStartIndex { get; set; } = -1;

        /// <summary>
        /// Defines the upper inclusive index bound of the targets to generate.
        /// 
        /// A value of -1 means no upper limit (include all remaining targets).
        /// 
        /// Example:
        ///     99 -> stop at the 100th target
        ///     -1 -> no upper bound
        /// </summary>
        public int TargetEndIndex { get; set; } = -1;

        public static bool IsValid(DownloaderSettings? settings)
        {
            if (settings is null)
                return false;

            if (!ValidateStringSettings(settings))
                return false;

            if (!ValidateIntegerSettings(settings))
                return false;

            return true;
        }

        private static bool ValidateIntegerSettings(DownloaderSettings settings)
        {
            NormalizeTargetBounds(settings);

            // Must be at least 1 — otherwise nothing downloads
            if (settings.MaxConcurrentDownloads < 1)
                return false;

            // Retries must not be negative
            if (settings.DownloadRetries < 0)
                return false;

            // Waiting time must not be negative
            if (settings.SecondsWaitBetweenRetry < 0)
                return false;

            return true;
        }

        private static void NormalizeTargetBounds(DownloaderSettings settings)
        {
            if (settings.TargetStartIndex < -1)
                settings.TargetStartIndex = -1;

            if (settings.TargetEndIndex < -1)
                settings.TargetEndIndex = -1;
        }

        private static bool ValidateStringSettings(DownloaderSettings settings)
        {
            if (!DoesRequiredStringSettingHaveValues(settings))
                return false;

            if (!AreRequiredStringSettingsRooted(settings))
                return false;

            if (!IsFilesToDownloadExcelInputExcelFile(settings))
                return false;

            return true;
        }

        private static bool AreRequiredStringSettingsRooted(DownloaderSettings settings)
        {
            if (!Path.IsPathRooted(settings.DownloadedFilesOutputPath))
                return false;

            if (!Path.IsPathRooted(settings.ReportsOutputPath))
                return false;

            if (!Path.IsPathRooted(settings.FilesToDownloadExcelInput))
                return false;
            return true;
        }

        private static bool DoesRequiredStringSettingHaveValues(DownloaderSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.DownloadedFilesOutputPath))
                return false;

            if (string.IsNullOrWhiteSpace(settings.ReportsOutputPath))
                return false;

            if (string.IsNullOrWhiteSpace(settings.FilesToDownloadExcelInput))
                return false;
            return true;
        }

        private static bool IsFilesToDownloadExcelInputExcelFile(DownloaderSettings settings)
        {
            string? ext = Path.GetExtension(settings.FilesToDownloadExcelInput);

            return ext?.ToLowerInvariant() switch
            {
                ".xlsx" => true,
                ".xlsm" => true,
                ".xls" => true,
                ".xlsb" => true,
                ".xltx" => true,
                ".xltm" => true,
                var _ => false,
            };
        }
    }
}