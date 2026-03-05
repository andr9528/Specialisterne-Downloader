using Downloader.Abstraction.Enum;
using Downloader.Abstraction.Interfaces.Model;

namespace Downloader.Model
{
    public class DownloadTarget : IDownloadTarget
    {
        /// <inheritdoc />
        public string OutputFileName { get; set; }

        /// <inheritdoc />
        public string? FullOutputFilePath { get; set; }

        /// <inheritdoc />
        public string PrimaryLink { get; set; }

        /// <inheritdoc />
        public string SecondaryLink { get; set; }

        /// <inheritdoc />
        public DownloadedUsing DownloadedUsing { get; set; }

        /// <inheritdoc />
        public TimeSpan? TimeToDownload { get; set; }

        /// <inheritdoc />
        public long OutputFileSize { get; set; }

        public string? FailureReason { get; set; }
    }
}