namespace FuelRecon.Application.Excel;

/// <summary>
/// Selects how the neutral workbook reader locates the header row when opening an .xlsx sheet.
/// </summary>
public enum ExcelWorkbookHeaderDetectionKind
{
    /// <summary>
    /// First row in the used range that contains any non-empty cell (legacy behaviour).
    /// </summary>
    FirstNonEmptyRow = 0,

    /// <summary>
    /// Scan from the top of the used range for a row that matches branch litres header aliases.
    /// </summary>
    BranchLitres = 1,

    /// <summary>
    /// Scan from the top of the used range for a row that matches Cars+ billing header aliases.
    /// </summary>
    CarsBilling = 2,
}

/// <summary>
/// Optional workbook reading behaviour; parsers pass detection profiles for real-world layouts.
/// </summary>
public sealed record ExcelWorkbookReadOptions(
    ExcelWorkbookHeaderDetectionKind HeaderDetectionKind = ExcelWorkbookHeaderDetectionKind.FirstNonEmptyRow);
