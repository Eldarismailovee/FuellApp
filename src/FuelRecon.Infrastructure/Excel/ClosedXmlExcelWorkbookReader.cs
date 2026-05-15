using ClosedXML.Excel;
using FuelRecon.Application.Excel;

namespace FuelRecon.Infrastructure.Excel;

/// <summary>
/// Reads trusted structure from MVP .xlsx workbooks without applying business parsing rules.
/// </summary>
public sealed class ClosedXmlExcelWorkbookReader : IExcelWorkbookReader
{
    public const string UnsupportedExcelFormatReasonCode = "UnsupportedExcelFormat";

    public const string ExcelFileNotFoundReasonCode = "ExcelFileNotFound";

    public const string ExcelReadFailedReasonCode = "ExcelReadFailed";

    public ExcelWorkbookReadResult ReadWorkbook(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return ExcelWorkbookReadResult.Failed(ExcelFileNotFoundReasonCode, "Excel file path cannot be empty.");
        }

        if (!string.Equals(Path.GetExtension(filePath), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return ExcelWorkbookReadResult.Failed(
                UnsupportedExcelFormatReasonCode,
                "Only .xlsx Excel workbooks are supported for the MVP.");
        }

        if (!File.Exists(filePath))
        {
            return ExcelWorkbookReadResult.Failed(ExcelFileNotFoundReasonCode, "Excel workbook was not found.");
        }

        try
        {
            using var workbook = new XLWorkbook(filePath);
            var sheets = workbook.Worksheets
                .Select(worksheet => ReadSheet(filePath, worksheet))
                .ToArray();

            return ExcelWorkbookReadResult.Succeeded(new ExcelWorkbookModel(filePath, sheets));
        }
        catch (Exception exception)
        {
            return ExcelWorkbookReadResult.Failed(
                ExcelReadFailedReasonCode,
                $"Excel workbook could not be read: {exception.Message}");
        }
    }

    private static ExcelSheetModel ReadSheet(string sourceFile, IXLWorksheet worksheet)
    {
        var range = worksheet.RangeUsed();
        if (range is null)
        {
            return new ExcelSheetModel(sourceFile, worksheet.Name, HeaderRowNumber: 0, [], []);
        }

        var firstRowNumber = range.RangeAddress.FirstAddress.RowNumber;
        var lastRowNumber = range.RangeAddress.LastAddress.RowNumber;
        var firstColumnNumber = range.RangeAddress.FirstAddress.ColumnNumber;
        var lastColumnNumber = range.RangeAddress.LastAddress.ColumnNumber;

        var headerRowNumber = FindHeaderRowNumber(worksheet, firstRowNumber, lastRowNumber, firstColumnNumber, lastColumnNumber);
        if (headerRowNumber is null)
        {
            return new ExcelSheetModel(sourceFile, worksheet.Name, HeaderRowNumber: 0, [], []);
        }

        var headers = Enumerable
            .Range(firstColumnNumber, lastColumnNumber - firstColumnNumber + 1)
            .Select(columnNumber => ReadCellText(worksheet.Cell(headerRowNumber.Value, columnNumber)).Trim())
            .ToArray();

        var rows = new List<ExcelRowModel>();
        for (var rowNumber = headerRowNumber.Value + 1; rowNumber <= lastRowNumber; rowNumber++)
        {
            var cells = Enumerable
                .Range(firstColumnNumber, headers.Length)
                .Select((columnNumber, index) => new ExcelCellModel(
                    sourceFile,
                    worksheet.Name,
                    rowNumber,
                    columnNumber,
                    Header: string.IsNullOrWhiteSpace(headers[index]) ? null : headers[index],
                    RawText: ReadCellText(worksheet.Cell(rowNumber, columnNumber))))
                .ToArray();

            if (cells.Any(cell => !string.IsNullOrWhiteSpace(cell.RawText)))
            {
                rows.Add(new ExcelRowModel(sourceFile, worksheet.Name, rowNumber, cells));
            }
        }

        return new ExcelSheetModel(sourceFile, worksheet.Name, headerRowNumber.Value, headers, rows);
    }

    private static int? FindHeaderRowNumber(
        IXLWorksheet worksheet,
        int firstRowNumber,
        int lastRowNumber,
        int firstColumnNumber,
        int lastColumnNumber)
    {
        for (var rowNumber = firstRowNumber; rowNumber <= lastRowNumber; rowNumber++)
        {
            var hasHeader = Enumerable
                .Range(firstColumnNumber, lastColumnNumber - firstColumnNumber + 1)
                .Any(columnNumber => !string.IsNullOrWhiteSpace(ReadCellText(worksheet.Cell(rowNumber, columnNumber))));

            if (hasHeader)
            {
                return rowNumber;
            }
        }

        return null;
    }

    private static string ReadCellText(IXLCell cell) => cell.CachedValue.ToString();
}
