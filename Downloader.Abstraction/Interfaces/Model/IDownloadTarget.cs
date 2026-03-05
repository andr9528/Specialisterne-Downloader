using Downloader.Abstraction.Enum;

namespace Downloader.Abstraction.Interfaces.Model
{
    public interface IDownloadTarget
    {
        string OutputFileName { get; set; }
        string? FullOutputFilePath { get; set; }
        string? PrimaryLink { get; set; }
        string? SecondaryLink { get; set; }
        DownloadedUsing DownloadedUsing { get; set; }
        TimeSpan? TimeToDownload { get; set; }
        long OutputFileSize { get; set; }
        public string? FailureReason { get; set; }
    }
}