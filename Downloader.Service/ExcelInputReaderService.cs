using ClosedXML.Excel;
using Downloader.Abstraction.Enum;
using Downloader.Abstraction.Interfaces.Model;
using Downloader.Abstraction.Interfaces.Services;
using Downloader.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Downloader.Service
{
    /// FEEDBACK
    /// S:
    /// O:
    /// L:
    /// I:
    /// D:
    /// Naming: In try methods I kinda expect a try and handling if the try fails
    /// Readability: Very readable
    /// Organisation: Nicely organized to private methods and easily readable from the main method
    /// Comments:
    /// Error Handling: Not Much error handling.
    /// Logging: Lot of logging
    /// Test Ideas:
    /// Other:
    public class ExcelInputReaderService : IInputReaderService
    {
        private readonly ILogger<ExcelInputReaderService> logger;
        private readonly IOptions<DownloaderSettings> options;
        private const int COL_OUTPUT_FILE_NAME = 1; // A
        private const int COL_PRIMARY_LINK = 38; // AL
        private const int COL_SECONDARY_LINK = 39; // AM

        public static int OutputFileNameColumnIndex => COL_OUTPUT_FILE_NAME;
        public static int PrimaryLinkColumnIndex => COL_PRIMARY_LINK;
        public static int SecondaryLinkColumnIndex => COL_SECONDARY_LINK;

        public ExcelInputReaderService(ILogger<ExcelInputReaderService> logger, IOptions<DownloaderSettings> options)
        {
            this.logger = logger;
            this.options = options;
        }

        /// <inheritdoc />
        public Task<IList<IDownloadTarget>> LoadTargets(string sourceFile)
        {
            int lowerBound = options.Value.TargetStartIndex;
            int upperBound = options.Value.TargetEndIndex;

            string path = ValidateExcelPath(sourceFile);

            using XLWorkbook workbook = OpenWorkbook(path);
            IXLWorksheet worksheet = GetWorksheet(workbook);

            var targets = ReadTargetsFromWorksheet(worksheet);
            var filteredTargets = ApplyTargetRange(targets, lowerBound, upperBound);

            return Task.FromResult<IList<IDownloadTarget>>(filteredTargets);
        }

        private List<IDownloadTarget> ApplyTargetRange(List<IDownloadTarget> targets, int lowerBound, int upperBound)
        {
            if (targets.Count == 0)
                return targets;

            int start = lowerBound < 0 ? 0 : lowerBound;
            int end = upperBound < 0 ? targets.Count - 1 : upperBound;

            if (end < start)
            {
                logger.LogWarning(
                    "Invalid target range. StartIndex: {StartIndex}, EndIndex: {EndIndex}. Returning 0 targets.",
                    lowerBound, upperBound);

                return new List<IDownloadTarget>();
            }

            // Clamp to available targets
            start = Math.Clamp(start, 0, targets.Count - 1);
            end = Math.Clamp(end, 0, targets.Count - 1);

            int count = end - start + 1;

            logger.LogInformation(
                "Applying target range. Requested: [{RequestedStart}..{RequestedEnd}] -> Applied: [{AppliedStart}..{AppliedEnd}] Count: {Count} out of {Total}.",
                lowerBound, upperBound, start, end, count, targets.Count);

            return targets.Skip(start).Take(count).ToList();
        }

        private string ValidateExcelPath(string sourceFile)
        {
            return !File.Exists(sourceFile)
                ? throw new FileNotFoundException($"Excel input file not found: {sourceFile}", sourceFile)
                : sourceFile;
        }

        private XLWorkbook OpenWorkbook(string path)
        {
            return new XLWorkbook(path);
        }

        //FEEDBACK: This could take from options which worksheet you want to use form a workbook
        private IXLWorksheet GetWorksheet(XLWorkbook workbook)
        {
            return workbook.Worksheets.First();
        }

        private List<IDownloadTarget> ReadTargetsFromWorksheet(IXLWorksheet worksheet)
        {
            int lastRow = GetLastUsedRowNumber(worksheet);

            if (lastRow < 2)
            {
                logger.LogWarning("Excel input contains no data rows (only headers or empty sheet).");
                return new List<IDownloadTarget>();
            }

            int lastColumn = GetLastUsedColumnNumber(worksheet);
            var targets = new List<IDownloadTarget>();

            for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
            {
                IXLRow row = worksheet.Row(rowNumber);

                if (!RowHasAnyTextContent(row, lastColumn))
                    continue;

                if (!TryCreateTargetFromRow(row, rowNumber, out IDownloadTarget target))
                    continue;

                targets.Add(target);
            }

            return targets;
        }

        private int GetLastUsedRowNumber(IXLWorksheet worksheet)
        {
            return worksheet.LastRowUsed()?.RowNumber() ?? 0;
        }

        private int GetLastUsedColumnNumber(IXLWorksheet worksheet)
        {
            return worksheet.LastColumnUsed()?.ColumnNumber() ?? 1;
        }

        /// <summary>
        /// Treat row as "having content" if ANY cell contains non-whitespace text.
        /// (Avoids "used cells" being affected by formatting.)
        /// </summary>
        /// <param name="row"></param>
        /// <param name="lastColumn"></param>
        /// <returns></returns>
        private bool RowHasAnyTextContent(IXLRow row, int lastColumn)
        {
            return row.Cells(1, lastColumn).Any(c => !string.IsNullOrWhiteSpace(c.GetString()));
        }

        private bool TryCreateTargetFromRow(IXLRow row, int rowNumber, out IDownloadTarget target)
        {
            string outputFileName = GetCellTrimmedOrEmpty(row, COL_OUTPUT_FILE_NAME);
            string primaryLink = GetCellTrimmedOrEmpty(row, COL_PRIMARY_LINK);
            string secondaryLink = GetCellTrimmedOrEmpty(row, COL_SECONDARY_LINK);

            if (string.IsNullOrWhiteSpace(outputFileName))
            {
                logger.LogWarning("Row {RowNumber} has data but cell A ({OutputFileName}) is empty. Skipping row.",
                    rowNumber, nameof(IDownloadTarget.OutputFileName));

                target = null!;
                return false;
            }

            target = new DownloadTarget
            {
                OutputFileName = outputFileName,
                PrimaryLink = primaryLink,
                SecondaryLink = secondaryLink,
                DownloadedUsing = DownloadedUsing.NONE,
            };

            return true;
        }

        private string GetCellTrimmedOrEmpty(IXLRow row, int columnNumber)
        {
            return row.Cell(columnNumber).GetString()?.Trim() ?? string.Empty;
        }
    }
}