namespace FuelRecon.Application.Excel;

public sealed record ExcelWorkbookReadResult(
    bool Success,
    ExcelWorkbookModel? Workbook,
    string? ReasonCode,
    string Message)
{
    public static ExcelWorkbookReadResult Succeeded(ExcelWorkbookModel workbook) =>
        new(Success: true, workbook, ReasonCode: null, Message: "Workbook read successfully.");

    public static ExcelWorkbookReadResult Failed(string reasonCode, string message) =>
        new(Success: false, Workbook: null, reasonCode, message);
}

public sealed record ExcelWorkbookModel(
    string SourceFile,
    IReadOnlyList<ExcelSheetModel> Sheets);

public sealed record ExcelSheetModel(
    string SourceFile,
    string SheetName,
    int HeaderRowNumber,
    IReadOnlyList<string> Headers,
    IReadOnlyList<ExcelRowModel> Rows);

public sealed record ExcelRowModel(
    string SourceFile,
    string SheetName,
    int RowNumber,
    IReadOnlyList<ExcelCellModel> Cells);

public sealed record ExcelCellModel(
    string SourceFile,
    string SheetName,
    int RowNumber,
    int ColumnNumber,
    string? Header,
    string RawText);
