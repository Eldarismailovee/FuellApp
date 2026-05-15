using ClosedXML.Excel;

namespace FuelRecon.Infrastructure.Excel;

internal static class ExcelHeaderRowDetector
{
    internal const int MaxRowsToScan = 80;

    internal static int? FindBranchLitresHeaderRow(
        IXLWorksheet worksheet,
        int firstRowNumber,
        int lastRowNumber,
        int firstColumnNumber,
        int lastColumnNumber)
    {
        var scanEnd = Math.Min(lastRowNumber, firstRowNumber + MaxRowsToScan - 1);
        for (var rowNumber = firstRowNumber; rowNumber <= scanEnd; rowNumber++)
        {
            var texts = ReadRowCellTexts(worksheet, rowNumber, firstColumnNumber, lastColumnNumber);
            if (IsBranchLitresHeaderRow(texts))
            {
                return rowNumber;
            }
        }

        return null;
    }

    internal static int? FindCarsBillingHeaderRow(
        IXLWorksheet worksheet,
        int firstRowNumber,
        int lastRowNumber,
        int firstColumnNumber,
        int lastColumnNumber)
    {
        var scanEnd = Math.Min(lastRowNumber, firstRowNumber + MaxRowsToScan - 1);
        for (var rowNumber = firstRowNumber; rowNumber <= scanEnd; rowNumber++)
        {
            var texts = ReadRowCellTexts(worksheet, rowNumber, firstColumnNumber, lastColumnNumber);
            if (IsCarsBillingHeaderRow(texts))
            {
                return rowNumber;
            }
        }

        return null;
    }

    private static bool IsBranchLitresHeaderRow(IReadOnlyList<string> cellTexts)
    {
        if (!RowMatchesAnyAlias(cellTexts, ExcelParserKnownHeaders.BranchLitres.Litres))
        {
            return false;
        }

        return RowMatchesAnyAlias(cellTexts, ExcelParserKnownHeaders.BranchLitres.Date)
            || RowMatchesAnyAlias(cellTexts, ExcelParserKnownHeaders.BranchLitres.RentalAgreement)
            || RowMatchesAnyAlias(cellTexts, ExcelParserKnownHeaders.BranchLitres.Rego)
            || RowMatchesAnyAlias(cellTexts, ExcelParserKnownHeaders.BranchLitres.Note);
    }

    private static bool IsCarsBillingHeaderRow(IReadOnlyList<string> cellTexts)
    {
        var hasIdentifier = RowMatchesAnyAlias(cellTexts, ExcelParserKnownHeaders.CarsBilling.RentalAgreement)
            || RowMatchesAnyAlias(cellTexts, ExcelParserKnownHeaders.CarsBilling.Rego);

        var hasBillingColumn = RowMatchesAnyAlias(cellTexts, ExcelParserKnownHeaders.CarsBilling.BilledLitres)
            || RowMatchesAnyAlias(cellTexts, ExcelParserKnownHeaders.CarsBilling.BilledAmount)
            || RowMatchesAnyAlias(cellTexts, ExcelParserKnownHeaders.CarsBilling.Status);

        return hasIdentifier && hasBillingColumn;
    }

    private static bool RowMatchesAnyAlias(IReadOnlyList<string> cellTexts, IReadOnlyCollection<string> aliases)
    {
        var aliasKeys = aliases
            .Select(ExcelColumnHeaderMatcher.Normalise)
            .Where(static key => key.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        if (aliasKeys.Count == 0)
        {
            return false;
        }

        foreach (var cellText in cellTexts)
        {
            var cellKey = ExcelColumnHeaderMatcher.Normalise(cellText);
            if (cellKey.Length > 0 && aliasKeys.Contains(cellKey))
            {
                return true;
            }
        }

        return false;
    }

    private static string[] ReadRowCellTexts(
        IXLWorksheet worksheet,
        int rowNumber,
        int firstColumnNumber,
        int lastColumnNumber)
    {
        var columnCount = lastColumnNumber - firstColumnNumber + 1;
        var texts = new string[columnCount];
        for (var offset = 0; offset < columnCount; offset++)
        {
            var columnNumber = firstColumnNumber + offset;
            texts[offset] = ReadCellText(worksheet.Cell(rowNumber, columnNumber)).Trim();
        }

        return texts;
    }

    private static string ReadCellText(IXLCell cell) => cell.CachedValue.ToString();
}
